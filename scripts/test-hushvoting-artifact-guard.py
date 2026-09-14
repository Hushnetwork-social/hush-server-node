"""Seeded leaks prove final-output scanning and private registration behavior."""
import importlib.util
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

SCRIPT = Path(__file__).with_name('hushvoting-artifact-guard.py')
SPEC = importlib.util.spec_from_file_location('artifact_guard', SCRIPT)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


class ArtifactTests(unittest.TestCase):
    def test_encoded_material_is_detected_without_echoing_it(self):
        with tempfile.TemporaryDirectory() as directory:
            guard = MODULE.Guard(directory)
            value = 'fixture <private> "value"'
            guard.register([value, 'ab' * 32])
            for content in [value, MODULE.html.escape(value), json.dumps(value)[1:-1], MODULE.quote(value, safe=''), ('ab' * 32).upper()]:
                Path(directory, 'result.trx').write_text(content)
                result = guard.scan()
                self.assertFalse(result['passed'])
                self.assertNotIn(value, json.dumps(result))
                self.assertNotIn('values', result)

    def test_mnemonic_list_and_capture_are_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            guard = MODULE.Guard(directory)
            words = ['abandon'] * 11 + ['about']
            guard.register([' '.join(words)])
            Path(directory, 'result.trx').write_text(json.dumps(words))
            self.assertFalse(guard.scan()['passed'])
            Path(directory, 'result.trx').write_text('safe summary')
            Path(directory, 'capture.zip').write_bytes(b'controlled capture fixture')
            self.assertFalse(guard.scan()['passed'])

    def run_supervisor(self, leak):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            output = base / 'artifacts'
            output.mkdir()
            child = base / 'child.py'
            child.write_text('''import json, os, socket
from pathlib import Path
def request(value):
    with socket.socket(socket.AF_UNIX) as s:
        s.connect(os.environ['HUSHVOTING_ARTIFACT_SOCKET'])
        s.sendall(json.dumps(value).encode() + b'\\n')
        assert json.loads(s.recv(1024))['ok']
request({'operation':'require'})
request({'operation':'register','values':['controlled-secret-fixture']})
request({'operation':'check'})
# Simulate the final TRX being written AFTER the scenario's own check.
value = 'controlled-secret-fixture' if os.environ['SEED_LEAK'] == 'yes' else 'safe result'
Path(os.environ['SEED_OUTPUT'], 'result.trx').write_text(value)
print(value)
''')
            import os
            result = subprocess.run([sys.executable, str(SCRIPT), '--output', str(output), '--report', str(output / 'scan.json'), '--', sys.executable, str(child)],
                env={**os.environ, 'SEED_OUTPUT': str(output), 'SEED_LEAK': 'yes' if leak else 'no'}, capture_output=True, text=True, timeout=15)
            report = json.loads((output / 'scan.json').read_text())
            self.assertNotIn('controlled-secret-fixture', result.stdout + result.stderr + json.dumps(report))
            self.assertEqual(report['registeredValues'], 1)
            self.assertTrue(report['requiredByScenario'])
            self.assertTrue(report['stdoutScanned'])
            return result.returncode, report

    def test_late_trx_and_stdout_leaks_fail_even_after_an_earlier_clean_check(self):
        code, report = self.run_supervisor(True)
        self.assertEqual(code, 1)
        self.assertFalse(report['passed'])
        self.assertEqual(report['violations'], 2)

    def test_clean_completed_output_passes(self):
        code, report = self.run_supervisor(False)
        self.assertEqual(code, 0)
        self.assertTrue(report['passed'])

    def test_registration_is_bounded(self):
        with tempfile.TemporaryDirectory() as directory:
            guard = MODULE.Guard(directory)
            for values in [['x'], ['x' * 65537], ['valid-value'] * 65, [None]]:
                with self.assertRaises(ValueError):
                    guard.register(values)

    def test_detached_command_descendant_is_terminated(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory)
            output = base / 'artifacts'
            output.mkdir()
            child = base / 'child.py'
            child.write_text('''import subprocess, sys, time
from pathlib import Path
p = subprocess.Popen([sys.executable, '-c', 'import time; time.sleep(20)'], start_new_session=True)
Path(sys.argv[1]).write_text(str(p.pid))
time.sleep(0.5)
''')
            pid_file = base / 'owned-pid.txt'
            subprocess.run([sys.executable, str(SCRIPT), '--output', str(output), '--report', str(output / 'scan.json'), '--', sys.executable, str(child), str(pid_file)],
                capture_output=True, text=True, timeout=15, check=True)
            stat = Path('/proc') / pid_file.read_text() / 'stat'
            # A dead child may briefly await reaping by the environment's init.
            self.assertTrue(not stat.exists() or stat.read_text().rsplit(')', 1)[1].split()[0] == 'Z')


if __name__ == '__main__':
    unittest.main()
