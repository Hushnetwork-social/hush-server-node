"""Fail-closed evidence-gate tests; synthetic receipts are never runtime evidence."""
import importlib.util
import json
from pathlib import Path
import tempfile
import unittest

spec = importlib.util.spec_from_file_location('release', Path(__file__).with_name('hushvoting-backend-release.py'))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)


class BackendReleaseEvidenceTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.folder = self.root / 'group'
        self.folder.mkdir()
        self.plan = {'group': {'original-id': 'Required behavior'}}
        self.build = {'sources': {'server': 's', 'client': 'c'}, 'artifacts': {'dotnet': 'd', 'frontend': 'f'}}
        self.evidence = self.root / 'backend-release.json'
        self.provenance = self.root / 'current.json'
        self.report = {'schema': 'hushvoting-backend-release-v1', 'cases': self.plan,
                       'completedGroups': ['group'], 'build': self.build}
        self.write(self.evidence, self.report)
        self.write(self.provenance, self.build)
        self.write(self.folder / 'group.provenance.json', self.build)
        self.scan = {'passed': True, 'violations': 0, 'stdoutScanned': True, 'checks': 2, 'requiredByScenario': True}
        self.write(self.folder / 'group.artifacts.json', self.scan)
        self.trx = self.folder / 'group.trx'
        self.trx.write_text('<TestRun><Results><UnitTestResult testName="Required behavior" outcome="Passed"/></Results></TestRun>')

    def write(self, path, value):
        path.write_text(json.dumps(value))

    def verify(self):
        return release.verify(self.evidence, self.provenance, self.plan)

    def test_accepts_exact_complete_matching_evidence(self):
        self.assertEqual(self.verify(), self.report)

    def test_reviewed_prerequisite_cannot_shrink_with_the_catalogue(self):
        release.require_reviewed_cases(release.REQUIRED_IDS | {'additional-case'})
        for sid in release.REQUIRED_IDS:
            with self.subTest(missing=sid), self.assertRaises(ValueError):
                release.require_reviewed_cases(release.REQUIRED_IDS - {sid})

    def test_rejects_missing_receipt(self):
        self.trx.unlink()
        with self.assertRaises(OSError): self.verify()

    def test_rejects_same_count_with_different_behavior(self):
        self.trx.write_text(self.trx.read_text().replace('Required behavior', 'Other behavior'))
        with self.assertRaises(ValueError): self.verify()

    def test_rejects_failed_skipped_and_duplicate_results(self):
        original = self.trx.read_text()
        for outcome in ('Failed', 'NotExecuted'):
            with self.subTest(outcome=outcome):
                self.trx.write_text(original.replace('Passed', outcome))
                with self.assertRaises(ValueError): self.verify()
        self.trx.write_text(original.replace('</Results>', '<UnitTestResult testName="Required behavior" outcome="Passed"/></Results>'))
        with self.assertRaises(ValueError): self.verify()

    def test_rejects_stale_current_build_or_individual_receipt(self):
        for path in (self.provenance, self.folder / 'group.provenance.json'):
            self.write(path, {**self.build, 'artifacts': {'dotnet': 'stale', 'frontend': 'f'}})
            with self.assertRaises(ValueError): self.verify()
            self.write(path, self.build)

    def test_rejects_incomplete_or_remapped_matrix(self):
        for change in ({'completedGroups': []}, {'cases': {'group': {'other-id': 'Required behavior'}}}):
            self.write(self.evidence, {**self.report, **change})
            with self.assertRaises(ValueError): self.verify()

    def test_rejects_unclean_or_incomplete_artifact_scan(self):
        for change in ({'passed': False}, {'violations': 1}, {'stdoutScanned': False}, {'checks': 1}):
            self.write(self.folder / 'group.artifacts.json', {**self.scan, **change})
            with self.assertRaises(ValueError): self.verify()


if __name__ == '__main__':
    unittest.main()
