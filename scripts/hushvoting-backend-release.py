#!/usr/bin/env python3
"""Run/verify the complete owned backend prerequisite, then its three Web consumers.

EPIC-001 -> FEAT-007 AC-076 / FEAT-008 AC-083 / FEAT-009 AC-087 ->
FEAT-011 Phase 3 Tasks 3.1-3.8 and migration Tasks 7.M1-7.M3.
This gate covers the named backend prerequisite, not whole-product release readiness.
"""
import argparse
import collections
import importlib.util
import json
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import uuid
import xml.etree.ElementTree as ET

SERVER = Path(__file__).resolve().parents[1]
AREA = SERVER / 'Node/HushNode.IntegrationTests/HushVoting'
GROUPS = (
    'HV-ID-BINDING-WAIT-TWIN', 'HV-ID-INGRESS-TWIN', 'HV-ID-ADMISSION-TWIN',
    'HV-NODE-RESTART-TWIN', 'HV-ID-ENCODING-TWIN', 'HV-ID-CACHE-TWIN',
    'HV-ID-SHAPE-TWIN', 'HV-ID-NULL-TWIN', 'HV-ID-CACHE-OUTAGE-TWIN',
    'HV-NODE-RESET-TWIN', 'HV-ID-FIELD-TWIN', 'HV-ORIGINAL-IDENTITY-TWIN',
)
# This reviewed prerequisite must not shrink merely because a test is deleted or
# loses its tag. Additional owned cases are included automatically by inventory.
REQUIRED_IDS = {
    *(f'HV-TWIN-ID-{family}-{number:03}' for family, count in (
        ('INGRESS', 9), ('ADMISSION', 4), ('RESTART', 2), ('ENCODING', 6),
        ('CACHE', 9), ('SHAPE', 8), ('NULL', 12), ('RESET', 2), ('FIELD', 21),
        ('BINDING', 1), ('WAIT', 1)) for number in range(1, count + 1)),
    'HV-DAT-SECURITY-AC081', 'HV-ID-CREATE-SECURITY-005', 'HV-RW-SECURITY-007',
}


def require_reviewed_cases(ids):
    if not REQUIRED_IDS.issubset(ids):
        raise ValueError('A reviewed backend release-prerequisite case is missing.')


def inventory():
    spec = importlib.util.spec_from_file_location('catalogue', SERVER / 'scripts/hushvoting-test-catalogue.py')
    catalogue = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(catalogue)
    rows = {}
    for path in (AREA / 'ServerTwins').glob('*.feature'):
        for title, tags in catalogue.scenario_tags(path).items():
            ids = [t[1:] for t in tags if t.startswith('@HV-TWIN-ID-')]
            if len(ids) != 1 or ids[0] in rows:
                raise ValueError('Invalid backend identity catalogue.')
            rows[ids[0]] = (title, tags)
    for file in json.loads((AREA / 'migration-manifest.json').read_text())['files']:
        actual = catalogue.scenario_tags(AREA / file['destination'])
        for scenario in file['scenarios']:
            if catalogue.execution_layer(scenario) == 'backend-twin':
                sid = catalogue.scenario_id(scenario)
                if sid in rows:
                    raise ValueError('Duplicate backend identity ID.')
                rows[sid] = (scenario['title'], actual[scenario['title']])
    require_reviewed_cases(rows.keys())
    plan = {group: {sid: title for sid, (title, tags) in rows.items() if '@' + group in tags}
            for group in GROUPS}
    selected = [sid for batch in plan.values() for sid in batch]
    if any(not batch for batch in plan.values()) or collections.Counter(selected) != collections.Counter(rows.keys()):
        raise ValueError('Focused groups must cover every backend identity scenario exactly once.')
    return plan


def snapshot(provenance):
    return {key: provenance[key] for key in ('sources', 'artifacts')}


def verify_group(folder, group, expected, build):
    tests = ET.parse(folder / (group + '.trx')).findall('.//{*}UnitTestResult')
    if (collections.Counter(t.get('testName') for t in tests) != collections.Counter(expected.values())
            or any(t.get('outcome') != 'Passed' for t in tests)):
        raise ValueError('Backend receipt has missing, duplicate, unexpected or nonpassing cases.')
    if snapshot(json.loads((folder / (group + '.provenance.json')).read_text())) != build:
        raise ValueError('Backend receipt does not match the running build.')
    scan = json.loads((folder / (group + '.artifacts.json')).read_text())
    if (scan.get('passed') is not True or scan.get('violations') != 0
            or scan.get('stdoutScanned') is not True or scan.get('checks', 0) < 1
            or (scan.get('requiredByScenario') and scan['checks'] < 2)):
        raise ValueError('Backend artifact protection did not pass.')


def verify(evidence, provenance, plan=None):
    plan = inventory() if plan is None else plan
    report = json.loads(evidence.read_text())
    build = snapshot(json.loads(provenance.read_text()))
    if (report.get('schema') != 'hushvoting-backend-release-v1'
            or report.get('build') != build or report.get('cases') != plan
            or report.get('completedGroups') != list(plan)):
        raise ValueError('Incomplete, stale or differently mapped backend prerequisite.')
    for group, expected in plan.items():
        verify_group(evidence.parent / group, group, expected, build)
    return report


def run_group(output, group, *, build=False, backend=False, evidence=None):
    folder = output / group
    folder.mkdir()
    env = os.environ.copy()
    for key in ('HUSH_TEST_KEYS_DIR', 'HUSH_TEST_DAT_PASSWORD', 'HUSH_TEST_CORPUS_INVENTORY',
                'HUSHVOTING_CONTROLLED_CORPUS', 'HUSHVOTING_BACKEND_RELEASE_EVIDENCE'):
        env.pop(key, None)
    env['HUSHVOTING_E2E_OUTPUT'] = str(folder)
    if evidence:
        env['HUSHVOTING_BACKEND_RELEASE_EVIDENCE'] = str(evidence)
    command = ['timeout', '--kill-after=30s', '900s' if build else '420s',
               'bash', str(SERVER / 'scripts/run-hushvoting-e2e.sh'), '--group', group]
    if build:
        command.append('--build')
    if backend:
        command.append('--server-twins')
    # The owned runner scans its stdout. Do not place command logs in a protected
    # scenario folder or replay their potentially sensitive contents on failure.
    with tempfile.NamedTemporaryFile(prefix='hushvoting-backend-release-', suffix='.txt', delete=False) as log:
        log_path = Path(log.name)
        child = subprocess.Popen(command, env=env, stdin=subprocess.DEVNULL,
                                 stdout=log, stderr=subprocess.STDOUT, start_new_session=True)
        try:
            code = child.wait()
        finally:
            if child.poll() is None:
                os.killpg(child.pid, signal.SIGTERM)
                try:
                    child.wait(timeout=35)
                except subprocess.TimeoutExpired:
                    os.killpg(child.pid, signal.SIGKILL)
                    child.wait()
        if code:
            print('Failed group command log retained outside protected artifacts: ' + str(log_path), flush=True)
            raise ValueError('Owned test group failed: ' + group + '; inspect its sanitized TRX/artifact report.')
    log_path.unlink()
    return folder


def run(output):
    plan = inventory()
    output.mkdir(parents=True, exist_ok=False)
    evidence = output / 'backend-release.json'
    report = {'schema': 'hushvoting-backend-release-v1', 'cases': plan,
              'completedGroups': [], 'build': None}
    for index, (group, expected) in enumerate(plan.items()):
        print('START ' + group + ': ' + str(len(expected)) + ' cases', flush=True)
        folder = run_group(output, group, build=index == 0, backend=True)
        current = snapshot(json.loads((folder / (group + '.provenance.json')).read_text()))
        if report['build'] is None:
            report['build'] = current
        verify_group(folder, group, expected, report['build'])
        report['completedGroups'].append(group)
        evidence.write_text(json.dumps(report, indent=2) + '\n')
        print('PASS ' + group, flush=True)
    stamp = SERVER / 'Node/HushNode.IntegrationTests/bin/Debug/hushvoting-e2e-build.json'
    verify(evidence, stamp, plan)
    print('Backend prerequisite verified; starting its three real Web consumers.', flush=True)
    web = run_group(output, 'HV-BACKEND-RELEASE', evidence=evidence)
    consumer_ids = {'@HV-ID-CREATE-SECURITY-008', '@HV-RW-SECURITY-012', '@HV-DAT-SECURITY-AC087'}
    consumers = {next(iter(consumer_ids.intersection(s['tags']))): s['title']
                 for f in json.loads((AREA / 'migration-manifest.json').read_text())['files']
                 for s in f['scenarios'] if consumer_ids.intersection(s['tags'])}
    if set(consumers) != consumer_ids:
        raise ValueError('The three original release-criterion consumers must be preserved.')
    verify_group(web, 'HV-BACKEND-RELEASE', consumers, report['build'])
    verify(evidence, stamp, plan)
    print('PASS backend prerequisite and Web release-criterion journeys: ' + str(output), flush=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('mode', choices=('run', 'verify'))
    parser.add_argument('--output', type=Path)
    parser.add_argument('--evidence', type=Path)
    parser.add_argument('--provenance', type=Path)
    args = parser.parse_args()
    if args.mode == 'verify':
        if not args.evidence or not args.provenance:
            parser.error('verify requires --evidence and --provenance')
        verify(args.evidence, args.provenance)
        print('Verified complete current-build backend prerequisite.')
    else:
        signal.signal(signal.SIGTERM, lambda *_: (_ for _ in ()).throw(InterruptedError()))
        run((args.output or SERVER / 'TestResults/HushVoting' / ('backend-release-' + str(uuid.uuid4()))).resolve())


if __name__ == '__main__':
    try:
        main()
    except (OSError, ValueError, KeyError, TypeError, ET.ParseError, InterruptedError, KeyboardInterrupt):
        raise SystemExit('Backend release prerequisite did not pass; no readiness approval is produced.') from None
