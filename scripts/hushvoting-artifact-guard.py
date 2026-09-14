"""Bounded foreground test supervisor; sensitive scan values stay in memory."""
import argparse
import html
import json
import os
from pathlib import Path
import re
import signal
import socket
import subprocess
import tempfile
import threading
from urllib.parse import quote

MAX_OUTPUT = 64 * 1024 * 1024
PROHIBITED = {'.png', '.jpg', '.jpeg', '.webm', '.mp4', '.zip', '.har', '.html', '.log'}


def process_start(pid):
    try:
        return Path(f'/proc/{pid}/stat').read_text().rsplit(')', 1)[1].split()[19]
    except (OSError, IndexError):
        return None


class Guard:
    def __init__(self, output):
        self.output = Path(output)
        self.values = set()
        self.required = False
        self.checks = 0
        self.protocol_error = False
        self.lock = threading.RLock()

    def register(self, values):
        if not isinstance(values, list) or len(values) > 64 or any(not isinstance(v, str) or not 4 <= len(v) <= 65536 for v in values):
            raise ValueError('Invalid scan registration')
        with self.lock:
            self.values.update(values)
            if len(self.values) > 4096 or sum(map(len, self.values)) > MAX_OUTPUT:
                raise ValueError('Scan registration limit exceeded')

    def patterns(self):
        variants = set()
        with self.lock:
            values = tuple(self.values)
        for value in values:
            variants.update([re.escape(value), re.escape(html.escape(value)), re.escape(quote(value, safe=''))])
            for ascii_only in (True, False):
                variants.add(re.escape(json.dumps(value, ensure_ascii=ascii_only)[1:-1]))
            if re.fullmatch(r'[0-9a-fA-F]{64,130}', value):
                variants.update([re.escape(value.lower()), re.escape(value.upper())])
            words = value.split()
            if len(words) in (12, 24) and all(re.fullmatch('[a-z]+', word) for word in words):
                variants.add(r'[\s\d,\"\[\]\-]+'.join(map(re.escape, words)))
        return re.compile('|'.join(sorted(variants, key=len, reverse=True))) if variants else None

    def scan(self, stdout=''):
        pattern = self.patterns()
        violations = int(bool(pattern and pattern.search(stdout)))
        files = 0
        total = 0
        for path in self.output.rglob('*'):
            if not path.is_file():
                continue
            files += 1
            size = path.stat().st_size
            total += size
            if path.is_symlink() or size > 16 * 1024 * 1024 or total > MAX_OUTPUT or path.suffix.lower() in PROHIBITED:
                violations += 1
                continue
            if path.suffix.lower() not in ('.trx', '.json', '.txt', '.xml'):
                violations += 1
                continue
            try:
                text = path.read_text(encoding='utf-8')
            except (OSError, UnicodeError):
                violations += 1
                continue
            violations += int(bool(pattern and pattern.search(text)))
        self.checks += 1
        return {'schema': 'hushvoting-artifact-scan-v1', 'passed': violations == 0 and not self.protocol_error,
                'filesScanned': files, 'registeredValues': len(self.values), 'violations': violations,
                'checks': self.checks, 'requiredByScenario': self.required}


def serve(listener, guard, stop):
    listener.settimeout(0.2)
    while not stop.is_set():
        try:
            connection, _ = listener.accept()
        except socket.timeout:
            continue
        with connection:
            connection.settimeout(3)
            try:
                data = bytearray()
                while b'\n' not in data:
                    chunk = connection.recv(65536)
                    if not chunk or len(data) + len(chunk) > 1024 * 1024:
                        raise ValueError('Invalid scan request')
                    data.extend(chunk)
                message = json.loads(data.partition(b'\n')[0])
                operation = message.get('operation')
                if operation == 'register':
                    guard.register(message['values'])
                    answer = {'ok': True}
                elif operation == 'require':
                    guard.required = True
                    answer = {'ok': True}
                elif operation == 'check':
                    result = guard.scan()
                    answer = {'ok': result['passed'] and result['registeredValues'] > 0}
                else:
                    raise ValueError('Unknown scan operation')
                connection.sendall(json.dumps(answer).encode() + b'\n')
            except Exception:
                guard.protocol_error = True
                try:
                    connection.sendall(b'{"ok":false}\n')
                except OSError:
                    pass


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--report', required=True, type=Path)
    parser.add_argument('command', nargs=argparse.REMAINDER)
    args = parser.parse_args()
    command = args.command[1:] if args.command[:1] == ['--'] else args.command
    if not command:
        raise SystemExit('Artifact supervisor needs a test command')
    guard = Guard(args.output)
    chunks = []
    overflow = threading.Event()
    stop = threading.Event()
    child = None
    owned = {}
    owned_lock = threading.Lock()

    def track_children():
        while not stop.is_set():
            with owned_lock:
                pending = list(owned.items())
            seen = set()
            while pending:
                pid, started = pending.pop()
                if pid in seen or process_start(pid) != started:
                    continue
                seen.add(pid)
                try:
                    children = Path(f'/proc/{pid}/task/{pid}/children').read_text().split()
                except OSError:
                    continue
                for value in children:
                    descendant = int(value)
                    birth = process_start(descendant)
                    if birth is not None:
                        with owned_lock:
                            owned[descendant] = birth
                        pending.append((descendant, birth))
            stop.wait(0.1)

    def terminate_owned(sig):
        with owned_lock:
            processes = list(owned.items())
        for pid, started in reversed(processes):
            if process_start(pid) == started:
                try:
                    os.kill(pid, sig)
                except ProcessLookupError:
                    pass

    def interrupted(_signum, _frame):
        raise KeyboardInterrupt

    signal.signal(signal.SIGTERM, interrupted)
    try:
        with tempfile.TemporaryDirectory(prefix='hv-artifacts-') as directory, socket.socket(socket.AF_UNIX) as listener:
            endpoint = str(Path(directory) / 'guard.sock')
            listener.bind(endpoint)
            os.chmod(endpoint, 0o600)
            listener.listen(8)
            server = threading.Thread(target=serve, args=(listener, guard, stop), daemon=True)
            server.start()
            env = {**os.environ, 'HUSHVOTING_ARTIFACT_SOCKET': endpoint}
            child = subprocess.Popen(command, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, start_new_session=True)
            owned[child.pid] = process_start(child.pid)
            tracker = threading.Thread(target=track_children, daemon=True)
            tracker.start()

            def drain():
                size = 0
                while chunk := child.stdout.read(65536):
                    size += len(chunk)
                    if size <= MAX_OUTPUT:
                        chunks.append(chunk)
                    else:
                        overflow.set()

            reader = threading.Thread(target=drain, daemon=True)
            reader.start()
            try:
                code = child.wait(timeout=290)
            finally:
                # Terminate every child of this bounded command, even after test exit.
                try:
                    os.killpg(child.pid, signal.SIGTERM)
                except ProcessLookupError:
                    pass
                # Playwright/browser children can create their own process
                # groups. Track descendants with start times to avoid PID reuse.
                terminate_owned(signal.SIGTERM)
                reader.join(timeout=3)
                try:
                    os.killpg(child.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                terminate_owned(signal.SIGKILL)
                child.wait(timeout=3)
                stop.set()
                server.join(timeout=4)
                tracker.join(timeout=2)
            stdout = b''.join(chunks).decode('utf-8', errors='replace')
            result = guard.scan(stdout)
            result['stdoutScanned'] = True
            result['passed'] &= not overflow.is_set() and not reader.is_alive() and (not guard.required or bool(guard.values))
            args.report.write_text(json.dumps(result, indent=2) + '\n')
            if not result['passed']:
                print('HushVoting artifact scan failed; sensitive diagnostics withheld. See the count-only scan report.')
                return 1
            print(stdout, end='')
            return code
    except (KeyboardInterrupt, subprocess.TimeoutExpired):
        print('HushVoting artifact-supervised test command was interrupted or timed out; diagnostics withheld.')
        return 1
    except Exception:
        print('HushVoting artifact supervisor failed; diagnostics withheld.')
        return 1
    finally:
        stop.set()
        terminate_owned(signal.SIGKILL)
        if child is not None and child.poll() is None:
            try:
                os.killpg(child.pid, signal.SIGKILL)
            except ProcessLookupError:
                pass
            child.wait(timeout=3)


if __name__ == '__main__':
    raise SystemExit(main())
