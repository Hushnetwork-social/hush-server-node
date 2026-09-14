"""Seeded selection defects: external gates cannot hide Web failures or become PASS."""
import importlib.util
import json
import re
from pathlib import Path
import unittest
from unittest.mock import patch

SPEC = importlib.util.spec_from_file_location("catalogue_selection", Path(__file__).with_name("hushvoting-test-catalogue.py"))
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
MANIFEST = Path(__file__).resolve().parents[1] / "Node/HushNode.IntegrationTests/HushVoting/migration-manifest.json"
READ = Path.read_text


class SelectionTests(unittest.TestCase):
    def changed(self, mutate):
        data = json.loads(MANIFEST.read_text())
        mutate(data)
        with patch.object(Path, "read_text", lambda path, *a, **k: json.dumps(data) if path == MANIFEST else READ(path, *a, **k)):
            return MODULE.selection(MANIFEST)

    @staticmethod
    def gate(data):
        return next(s for f in data["files"] for s in f["scenarios"] if "@HV-RW-SECURITY-014" in s["tags"])

    def test_all_original_entries_remain_accounted_for(self):
        result = MODULE.selection(MANIFEST)
        self.assertEqual(len(result["webScenarioIds"]), 206)
        self.assertEqual(len(result["frontendTwins"]), 68)
        retained = {item['scenarioId'] for item in result['frontendTwins']}
        self.assertTrue(retained.isdisjoint(result['webScenarioIds']))
        self.assertTrue(retained.isdisjoint(result['backendScenarioIds']))
        self.assertTrue(retained.isdisjoint(MODULE.QUALIFICATION_GATES))
        self.assertEqual({item['acceptance'] for item in result['frontendTwins']}, {'NOT_ESTABLISHED_BY_PLACEMENT'})
        self.assertEqual(set(result["backendScenarioIds"]), set(MODULE.BACKEND_CRITERIA))
        self.assertTrue(set(result["webScenarioIds"]).isdisjoint(result["backendScenarioIds"]))
        self.assertEqual(len(result["externalQualifications"]), 3)
        self.assertEqual({s["status"] for s in result["externalQualifications"]}, {"NOT_SUPPLIED"})
        self.assertEqual(result["qualificationAcceptance"], "NOT_ESTABLISHED_BY_WEB_EXECUTION")
        self.assertTrue(set(result["webScenarioIds"]).isdisjoint(MODULE.QUALIFICATION_GATES))

    def test_arbitrary_web_scenario_cannot_be_reclassified(self):
        def mutate(data):
            data["files"][0]["scenarios"][0]["externalQualification"] = {"kind": "independent-security-review", "status": "NOT_SUPPLIED"}
        with self.assertRaisesRegex(ValueError, "Unreviewed"):
            self.changed(mutate)

    def test_qualification_cannot_claim_web_pass(self):
        with self.assertRaisesRegex(ValueError, "cannot close"):
            self.changed(lambda data: self.gate(data).update(status="passed"))

    def test_qualification_cannot_attach_a_web_receipt(self):
        with self.assertRaisesRegex(ValueError, "cannot close"):
            self.changed(lambda data: self.gate(data).update(evidence="fake.trx"))

    def test_qualification_cannot_claim_supplied_evidence(self):
        with self.assertRaisesRegex(ValueError, "truthful NOT_SUPPLIED"):
            self.changed(lambda data: self.gate(data)["externalQualification"].update(status="PASS"))

    def test_qualification_criterion_cannot_change(self):
        with self.assertRaisesRegex(ValueError, "criterion changed"):
            self.changed(lambda data: self.gate(data).update(tags=["@FEAT-008", "@AC-008-001", "@HV-RW-SECURITY-014"]))

    def test_removing_exclusion_tag_is_rejected(self):
        target = MANIFEST.parent / "Features/recovery-words/resume-nav-owner-cleanup-migration-security.feature"
        with patch.object(Path, "read_text", lambda path, *a, **k: READ(path, *a, **k).replace(MODULE.QUALIFICATION_TAG, "") if path == target else READ(path, *a, **k)):
            with self.assertRaisesRegex(ValueError, "tag/manifest mismatch"):
                MODULE.selection(MANIFEST)

    def test_missing_qualification_entry_is_rejected(self):
        def mutate(data):
            for file in data["files"]:
                file["scenarios"] = [s for s in file["scenarios"] if "@HV-RW-SECURITY-014" not in s["tags"]]
        with self.assertRaisesRegex(ValueError, "inventory mismatch"):
            self.changed(mutate)

    def test_epic_original_cannot_be_reclassified_as_backend_only(self):
        def mutate(data):
            scenario = next(s for f in data['files'] for s in f['scenarios'] if '@AT-LIC-001' in s['tags'])
            scenario['evidenceLayer'] = 'backend-twin'
        with self.assertRaisesRegex(ValueError, 'does not authorize backend-only'):
            self.changed(mutate)

    def test_unrelated_web_original_cannot_hide_in_backend_selection(self):
        with self.assertRaisesRegex(ValueError, 'does not authorize backend-only'):
            self.changed(lambda data: data['files'][0]['scenarios'][0].update(evidenceLayer='backend-twin'))

    def test_unknown_evidence_layer_is_rejected(self):
        with self.assertRaisesRegex(ValueError, 'Unknown executable evidence layer'):
            self.changed(lambda data: data['files'][0]['scenarios'][0].update(evidenceLayer='skipped'))

    def test_backend_original_requires_its_exact_criterion(self):
        def mutate(data):
            scenario = next(s for f in data['files'] for s in f['scenarios'] if '@HV-RW-SECURITY-007' in s['tags'])
            scenario['tags'].remove('@AC-008-076')
        with self.assertRaisesRegex(ValueError, 'does not authorize backend-only'):
            self.changed(mutate)

    def test_inherited_web_tag_cannot_start_both_fixtures(self):
        target = MANIFEST.parent / 'Features/recovery-words/resume-nav-owner-cleanup-migration-security.feature'
        with patch.object(Path, 'read_text', lambda path, *a, **k: '@HV-E2E\n'+READ(path, *a, **k) if path == target else READ(path, *a, **k)):
            with self.assertRaisesRegex(ValueError, 'layer/tag mismatch'):
                MODULE.selection(MANIFEST)

    def test_original_group_must_use_its_declared_runtime_layer(self):
        result = MODULE.selection(MANIFEST)
        backend = result['backendScenarioIds'][0]
        web = result['webScenarioIds'][0]
        MODULE.validate_group(result, backend, True)
        MODULE.validate_group(result, web, False)
        with self.assertRaisesRegex(ValueError, 'requires --server-twins'):
            MODULE.validate_group(result, backend, False)
        with self.assertRaisesRegex(ValueError, 'requires browser/server'):
            MODULE.validate_group(result, web, True)

    def test_retained_frontend_ids_are_excluded_from_feature_wide_web_execution(self):
        result = MODULE.selection(MANIFEST)
        excluded = set(MODULE.web_filter(result).split('&'))
        self.assertEqual(excluded - set(MODULE.WEB_FILTER.split('&')),
                         {'Category!=' + item['scenarioId'] for item in result['frontendTwins']})
        for item in result['frontendTwins']:
            for backend in [False, True]:
                with self.assertRaisesRegex(ValueError, 'TypeScript HushVotingApp TwinTest'):
                    MODULE.validate_group(result, item['scenarioId'], backend)

    def test_epic_cannot_be_hidden_as_a_retained_frontend_twin(self):
        def mutate(data):
            scenario = next(s for f in data['files'] for s in f['scenarios'] if '@AT-LIC-001' in s['tags'])
            scenario.update(migrationDisposition=MODULE.FRONTEND_DISPOSITION, status='pending-implementation')
        with self.assertRaisesRegex(ValueError, 'FEAT-only'):
            self.changed(mutate)

    def test_retention_requires_the_original_frontend_source_and_id(self):
        def mutate(data):
            scenario = next(s for f in data['files'] for s in f['scenarios'] if 'migrationDisposition' in s)
            scenario['frontendSource'] = 'hush-voting-web-client/missing.feature'
        with self.assertRaisesRegex(ValueError, 'source/ID'):
            self.changed(mutate)

    def test_retention_cannot_turn_placement_into_a_passing_result(self):
        def mutate(data):
            scenario = next(s for f in data['files'] for s in f['scenarios'] if 'migrationDisposition' in s)
            scenario['status'] = 'passed'
        with self.assertRaisesRegex(ValueError, 'outstanding FEAT-only'):
            self.changed(mutate)

    def test_frontend_package_web_commands_still_select_executable_scenarios(self):
        result = MODULE.selection(MANIFEST)
        retained = {'@' + item['scenarioId'] for item in result['frontendTwins']}
        executable = []
        for path in MANIFEST.parent.rglob('*.feature'):
            for tags in MODULE.scenario_tags(path).values():
                tags = set(tags)
                if '@HV-E2E' in tags and MODULE.QUALIFICATION_TAG not in tags and not retained.intersection(tags):
                    executable.append(tags)
        package = MANIFEST.resolve().parents[4] / 'hush-voting-web-client/package.json'
        for name, command in json.loads(package.read_text())['scripts'].items():
            selected = re.search(r'--group ([A-Za-z0-9_-]+)', command)
            if selected and '--server-twins' not in command:
                self.assertTrue(any('@' + selected[1] in tags for tags in executable), name)


if __name__ == "__main__":
    unittest.main()
