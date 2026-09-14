#!/usr/bin/env python3
"""Bind HushVoting receipts to the source and artifacts actually built and executed."""
import argparse
import hashlib
import json
import subprocess
from datetime import datetime, timezone
from pathlib import Path

SOURCE_SUFFIXES = {'.cs', '.csproj', '.sln', '.slnx', '.props', '.targets', '.proto', '.feature', '.json', '.ts', '.tsx', '.js', '.mjs', '.cjs', '.css', '.html', '.wasm', '.sh', '.py'}
IGNORED_PARTS = {'node_modules', 'bin', 'obj', 'target', 'TestResults', '.next', '.next-web', '.next-tauri', '.next-static', 'coverage', '.git', '.hepha'}

def digest(paths, root):
    result = hashlib.sha256()
    for path in sorted(paths):
        result.update(path.relative_to(root).as_posix().encode() + b'\0')
        with path.open('rb') as handle:
            result.update(hashlib.file_digest(handle, 'sha256').digest())
    return result.hexdigest()

def sources(root):
    names = subprocess.check_output(['git', '-C', str(root), 'ls-files', '--cached', '--others', '--exclude-standard', '-z']).decode().split('\0')
    paths = []
    for name in set(names):
        path = root / name
        if not name or not path.is_file() or path.suffix not in SOURCE_SUFFIXES or IGNORED_PARTS.intersection(Path(name).parts):
            continue
        if path.name in {'migration-manifest.json', 'next-env.d.ts'} or name.endswith('.feature.cs') or name.startswith(('public/workers/', 'TestResults/', 'tests/integration/')):
            continue
        paths.append(path)
    return digest(paths, root)

def source_snapshot(server, client):
    return {'server': sources(server), 'client': sources(client)}

def frontend_artifact(client):
    standalone = client / '.next-web/standalone'
    frontend = [standalone / 'server.js']
    for directory in (standalone / '.next-web/server', standalone / '.next-web/static', standalone / 'public', standalone / 'src/app/api/protos'):
        frontend.extend(p for p in directory.rglob('*') if p.is_file())
    return digest(frontend, standalone)

def artifacts(server, client):
    binary = server / 'Node/HushNode.IntegrationTests/bin/Debug'
    standalone = client / '.next-web/standalone'
    if not (binary / 'HushNode.IntegrationTests.dll').is_file() or not (standalone / 'server.js').is_file():
        raise SystemExit('HushVoting execution artifacts are missing; run with --build.')
    dotnet = [p for p in binary.rglob('*') if p.is_file() and p.suffix in {'.dll', '.json'} and p.name != 'hushvoting-e2e-build.json']
    return {'dotnet': digest(dotnet, binary), 'frontend': frontend_artifact(client)}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=['inputs', 'stamp', 'verify', 'frontend-current'])
    parser.add_argument('--server', required=True, type=Path)
    parser.add_argument('--client', required=True, type=Path)
    parser.add_argument('--stamp', required=True, type=Path)
    parser.add_argument('--before', type=Path)
    parser.add_argument('--receipt', type=Path)
    args = parser.parse_args()
    current = source_snapshot(args.server, args.client)
    if args.mode == 'frontend-current':
        try:
            stamp = json.loads(args.stamp.read_text())
            reusable = stamp.get('schemaVersion') == 1 and stamp.get('sources', {}).get('client') == current['client'] and stamp.get('artifacts', {}).get('frontend') == frontend_artifact(args.client)
        except (OSError, ValueError, TypeError):
            reusable = False
        print('yes' if reusable else 'no')
        return
    if args.mode == 'inputs':
        print(json.dumps(current, sort_keys=True))
        return
    if args.mode == 'stamp':
        if args.before is None or json.loads(args.before.read_text()) != current:
            raise SystemExit('HushVoting source changed during the build; build again before executing.')
        stamp = {'schemaVersion': 1, 'builtAtUtc': datetime.now(timezone.utc).isoformat(), 'sources': current, 'artifacts': artifacts(args.server, args.client)}
        args.stamp.write_text(json.dumps(stamp, indent=2) + '\n')
    else:
        if not args.stamp.is_file():
            raise SystemExit('HushVoting build provenance is missing; run with --build.')
        stamp = json.loads(args.stamp.read_text())
        if stamp.get('sources') != current or stamp.get('artifacts') != artifacts(args.server, args.client):
            raise SystemExit('HushVoting source or runtime artifacts changed since the build; run with --build.')
        if args.receipt:
            args.receipt.write_text(json.dumps({'verifiedAtUtc': datetime.now(timezone.utc).isoformat(), **stamp}, indent=2) + '\n')

if __name__ == '__main__':
    main()
