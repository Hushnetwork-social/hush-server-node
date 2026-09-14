"""FEAT-009 Phase 6 Task 6.10: controlled CLI refusals using public inputs only."""
import os
from pathlib import Path
import signal
import subprocess
import tempfile
import unittest


class ControlledRunnerTests(unittest.TestCase):
    def refuse(self, args, expected, missing=False):
        with tempfile.TemporaryDirectory(prefix='hv-corpus-cli-public-') as directory:
            root = Path(directory)
            guards = root / 'bin'
            guards.mkdir()
            # A regression must not start real builds, browsers or containers.
            for name in ('dotnet', 'npm', 'docker'):
                executable = guards / name
                executable.write_text('#!/bin/sh\nexit 99\n')
                executable.chmod(0o700)
            env = {k: v for k, v in os.environ.items() if not k.startswith('HUSH_TEST_') and k not in
                   ('HUSHVOTING_CONTROLLED_CORPUS', 'HUSHVOTING_E2E_OUTPUT')}
            env.update(PATH=str(guards) + os.pathsep + env['PATH'], HUSHVOTING_E2E_OUTPUT=str(root / 'output'))
            if not missing:
                env.update(HUSH_TEST_KEYS_DIR=str(root / 'unopened'), HUSH_TEST_DAT_PASSWORD='public-sentinel-password',
                           HUSH_TEST_CORPUS_INVENTORY=str(root / 'unopened.json'))
            process = subprocess.Popen(['bash', str(Path(__file__).with_name('run-hushvoting-e2e.sh')), *args],
                                       env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE, start_new_session=True)
            try:
                stdout, stderr = process.communicate(timeout=10)
                self.assertEqual(process.returncode, 2)
                self.assertIn(expected, stderr.decode())
                self.assertNotIn(b'public-sentinel-password', stdout + stderr)
                self.assertFalse((root / 'output').exists())
            finally:
                try:
                    os.killpg(process.pid, signal.SIGKILL)
                except ProcessLookupError:
                    pass
                process.wait(timeout=3)

    def test_unrelated_group_is_refused(self):
        self.refuse(['--controlled-corpus', '--group', 'HV-AUTH-LIFECYCLE'], 'requires exactly one original')

    def test_all_is_refused(self):
        self.refuse(['--controlled-corpus', '--all'], 'requires exactly one original')

    def test_backend_mode_is_refused(self):
        self.refuse(['--controlled-corpus', '--server-twins', '--group', 'HV-DAT-EXTERNAL-AC074'], 'requires exactly one original')

    def test_missing_inputs_are_refused(self):
        self.refuse(['--controlled-corpus', '--group', 'HV-DAT-EXTERNAL-AC074'], 'runtime inputs are missing', missing=True)

    def test_unowned_output_is_refused(self):
        self.refuse(['--controlled-corpus', '--group', 'HV-DAT-EXTERNAL-AC074'], 'owned default output')


if __name__ == '__main__':
    unittest.main()
