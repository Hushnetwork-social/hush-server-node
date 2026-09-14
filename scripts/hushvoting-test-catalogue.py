#!/usr/bin/env python3
"""Select Web execution separately from user-approved external qualification gates.

EPIC-001 / FEAT-011 / Phase 7 Task 7.M3. Qualification ownership stays with
FEAT-008 AC-008-081/085 and FEAT-009 AC-009-089. Reporting absence is not PASS.
"""
import argparse
import json
from pathlib import Path


QUALIFICATION_TAG = "@HV-EXTERNAL-QUALIFICATION"
FRONTEND_DISPOSITION = "retained-typescript-frontend-twin"
WEB_FILTER = "Category=HushVoting&Category=HV-E2E&Category!=HV-EXTERNAL-QUALIFICATION"
# These original FEAT criteria explicitly require server Twins. This is an
# executable layer selection, never an exemption or an EPIC acceptance shortcut.
BACKEND_CRITERIA = {
    "HV-ID-CREATE-SECURITY-005": "AC-007-071",
    "HV-RW-SECURITY-007": "AC-008-076",
    "HV-DAT-SECURITY-AC081": "AC-009-081",
}
# Reviewed classification, not a generic escape hatch for failing Web tests.
QUALIFICATION_GATES = {
    "HV-RW-PASSKEY-007": ("AC-008-081", "physical-device-prf"),
    "HV-RW-SECURITY-014": ("AC-008-085", "independent-security-review"),
    "HV-DAT-SECURITY-AC089": ("AC-009-089", "independent-security-review"),
}


def scenario_id(scenario):
    ids = [tag[1:] for tag in scenario["tags"] if tag.startswith(("@HV-", "@AT-LIC-"))]
    if len(ids) != 1:
        raise ValueError("Scenario must preserve one original stable ID: " + scenario["title"])
    return ids[0]


def qualification_entry(scenario):
    sid = scenario_id(scenario)
    expected = QUALIFICATION_GATES.get(sid)
    declaration = scenario.get("externalQualification")
    if expected is None:
        if declaration is not None or scenario["status"] == "pending-external-qualification":
            raise ValueError("Unreviewed external qualification classification: " + sid)
        return None
    criterion, kind = expected
    if "@" + criterion not in scenario["tags"]:
        raise ValueError("External qualification criterion changed: " + sid)
    if declaration != {"kind": kind, "status": "NOT_SUPPLIED"}:
        raise ValueError("Qualification requires its declared kind and truthful NOT_SUPPLIED status: " + sid)
    if scenario["status"] != "pending-external-qualification" or scenario.get("evidence"):
        raise ValueError("Web runtime evidence cannot close an external qualification gate: " + sid)
    return {"scenarioId": sid, "acceptanceId": criterion, **declaration}


def execution_layer(scenario):
    layer = scenario.get("evidenceLayer", "web-e2e")
    sid = scenario_id(scenario)
    if layer not in {"web-e2e", "backend-twin"}:
        raise ValueError("Unknown executable evidence layer: " + sid)
    if layer == "backend-twin" and (sid not in BACKEND_CRITERIA or "@" + BACKEND_CRITERIA[sid] not in scenario["tags"]):
        raise ValueError("Original criterion does not authorize backend-only evidence: " + sid)
    return layer


def frontend_entry(scenario, file, manifest_path):
    """Preserve the user's frontend ownership decision without asserting a pass."""
    disposition = scenario.get("migrationDisposition")
    if disposition is None:
        return None
    sid = scenario_id(scenario)
    if disposition != FRONTEND_DISPOSITION:
        raise ValueError("Unknown migration disposition: " + sid)
    tags = scenario["tags"]
    if (not any(t.startswith("@FEAT-") for t in tags)
            or not any(t.startswith("@AC-") for t in tags)
            or any(t.startswith(("@EPIC-", "@AT-LIC-")) for t in tags)
            or execution_layer(scenario) != "web-e2e"
            or scenario["status"] != "pending-implementation"
            or scenario.get("externalQualification")):
        raise ValueError("Frontend retention requires an outstanding FEAT-only mapping: " + sid)
    expected = "hush-voting-web-client/" + file["source"]
    workspace = manifest_path.resolve().parents[4]
    source = workspace / expected
    if (scenario.get("frontendSource") != expected
            or not source.resolve().is_relative_to(workspace / "hush-voting-web-client")
            or not source.is_file()
            or "@" + sid not in source.read_text().split()):
        raise ValueError("Retained frontend source/ID is missing or inconsistent: " + sid)
    return {"scenarioId": sid, "acceptanceIds": [t[1:] for t in tags if t.startswith("@AC-")],
            "source": expected, "status": scenario["status"],
            "acceptance": "NOT_ESTABLISHED_BY_PLACEMENT"}


def web_filter(result):
    # Feature-wide commands must respect the same ownership as --all. The
    # retained .NET catalogue copies remain available for explicit diagnostics
    # through raw dotnet selection; they are not frontend replacement evidence.
    return WEB_FILTER + "".join("&Category!=" + item["scenarioId"] for item in result["frontendTwins"])


def validate_layer_tags(scenario, tags):
    layer = execution_layer(scenario)
    required, forbidden = (({"@HV-SERVER-TWIN", "@NON_E2E"}, {"@HV-E2E", "@E2E"}) if layer == "backend-twin"
                           else ({"@HV-E2E", "@E2E"}, {"@HV-SERVER-TWIN"}))
    if not required.issubset(tags) or forbidden.intersection(tags):
        raise ValueError("Execution layer/tag mismatch: " + scenario_id(scenario))
    return layer


def validate_group(result, group, server_twins):
    if any(item["scenarioId"] == group for item in result["frontendTwins"]):
        raise ValueError("Selected entry remains a TypeScript HushVotingApp TwinTest; use the frontend runner: " + group)
    if group == QUALIFICATION_TAG[1:] or any(g["scenarioId"] == group for g in result["externalQualifications"]):
        raise ValueError("Selected entry is an external gate; use --qualifications, not runtime execution")
    if group in result["backendScenarioIds"] and not server_twins:
        raise ValueError("Selected original requires --server-twins: " + group)
    if group in result["webScenarioIds"] and server_twins:
        raise ValueError("Selected original requires browser/server execution: " + group)


def scenario_tags(path):
    tags, inherited, result = [], [], {}
    for line in path.read_text().splitlines():
        text = line.strip()
        if text.startswith("@"):
            tags.extend(text.split())
        elif text.startswith("Feature:"):
            inherited, tags = tags, []
        elif text.startswith("Scenario:"):
            title = text.removeprefix("Scenario:").strip()
            if title in result:
                raise ValueError("Duplicate scenario title: " + title)
            result[title], tags = inherited + tags, []
    return result


def selection(manifest_path):
    manifest = json.loads(manifest_path.read_text())
    seen, web, backend, gates, frontend = set(), [], [], [], []
    for file in manifest["files"]:
        actual = scenario_tags(manifest_path.parent / file["destination"])
        declared = file["scenarios"]
        if len(actual) != len(declared) or set(actual) != {s["title"] for s in declared}:
            raise ValueError("Destination inventory mismatch: " + file["destination"])
        for scenario in declared:
            sid = scenario_id(scenario)
            if sid in seen:
                raise ValueError("Duplicate scenario execution ID: " + sid)
            seen.add(sid)
            tags = actual[scenario["title"]]
            layer = validate_layer_tags(scenario, tags)
            if "@" + sid not in tags:
                raise ValueError("Destination stable ID missing: " + sid)
            gate = qualification_entry(scenario)
            retained = frontend_entry(scenario, file, manifest_path)
            if (QUALIFICATION_TAG in tags) != (gate is not None):
                raise ValueError("Qualification tag/manifest mismatch: " + sid)
            if gate:
                gates.append(gate)
            elif retained:
                frontend.append(retained)
            elif layer == "backend-twin":
                backend.append(sid)
            else:
                web.append(sid)
    if len(seen) != manifest["sourceScenarioCount"] or not set(QUALIFICATION_GATES).issubset(seen):
        raise ValueError("Original scenario or qualification inventory is incomplete")
    return {"webScenarioIds": web, "backendScenarioIds": backend, "frontendTwins": frontend, "externalQualifications": gates,
            "qualificationAcceptance": "NOT_ESTABLISHED_BY_WEB_EXECUTION"}


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("manifest", type=Path)
    parser.add_argument("--web-ids", action="store_true")
    parser.add_argument("--web-filter", action="store_true")
    parser.add_argument("--frontend-twins", action="store_true")
    parser.add_argument("--qualifications", action="store_true")
    parser.add_argument("--check-group")
    parser.add_argument("--server-twins", action="store_true")
    args = parser.parse_args()
    try:
        result = selection(args.manifest)
        if args.check_group:
            validate_group(result, args.check_group, args.server_twins)
        elif args.web_ids:
            print("\n".join(result["webScenarioIds"]))
        elif args.web_filter:
            print(web_filter(result))
        elif args.frontend_twins:
            print(json.dumps(result["frontendTwins"], indent=2))
        elif args.qualifications:
            print(json.dumps({key: value for key, value in result.items() if key not in {"webScenarioIds", "backendScenarioIds", "frontendTwins"}}, indent=2))
        else:
            print(json.dumps(result, indent=2))
    except (OSError, KeyError, ValueError) as error:
        parser.exit(1, "HushVoting catalogue selection rejected: " + str(error) + "\n")
