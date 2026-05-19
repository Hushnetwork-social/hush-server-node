using System.Text.Json.Nodes;

namespace SecurityDependencySupportReadinessPromoter;

public static class SecurityDependencySupportChecker
{
    public static SecurityDependencySupportCheckSet Evaluate(
        SecurityDependencySupportPromotionPaths paths,
        SecurityDependencySupportSourceSet? sources = null,
        DateTimeOffset? generatedAt = null)
    {
        sources ??= SecurityDependencySupportContracts.LoadSources(paths);
        var now = generatedAt ?? TryParseTimestamp(sources.Package, "generatedAt") ?? DateTimeOffset.UtcNow;
        var checks = new List<SecurityDependencySupportCheck>
        {
            CheckReleaseScopeBinding(sources),
            CheckScannerProvenanceCompleteness(sources),
            CheckDependencyInventoryCompleteness(sources),
            CheckThirdPartyLicenseGate(sources),
            CheckVulnerabilityThresholdAndFreshness(sources, now),
            CheckDisclosureProcessGate(sources),
            CheckAccessibilityEvidenceGate(sources),
            CheckVoterClientIntegrityGate(sources),
            CheckSupportReadinessGate(sources),
            CheckSupportExportPrivacyGate(sources),
            CheckExceptionHandoffAndClaimImpactGate(sources),
        };

        var blockers = checks
            .Where(check => check.Status == "blocked")
            .Select(check => check.CheckId)
            .ToArray();
        var warnings = checks
            .Where(check => check.Status == "warning")
            .Select(check => check.CheckId)
            .ToArray();
        var notApplicable = checks
            .Where(check => check.Status == "not_applicable")
            .Select(check => check.CheckId)
            .ToArray();
        var forbidden = SecurityDependencySupportContracts
            .ScanForbiddenMaterial(sources.SupportExportPrivacyProof, "restricted", "support-export-privacy-proof.json")
            .ToArray();
        if (forbidden.Length > 0 && !blockers.Contains("SDS-009", StringComparer.Ordinal))
        {
            blockers = [.. blockers, "SDS-009"];
        }

        var status = blockers.Length > 0 || forbidden.Length > 0
            ? "blocked"
            : warnings.Length > 0
                ? "accepted_with_warnings"
                : "accepted";

        return new SecurityDependencySupportCheckSet(
            status,
            checks,
            blockers,
            warnings,
            notApplicable,
            forbidden);
    }

    private static SecurityDependencySupportCheck CheckReleaseScopeBinding(SecurityDependencySupportSourceSet sources)
    {
        var releaseScope = SecurityDependencySupportContracts.RequireObject(sources.Package, "releaseScope");
        if (SecurityDependencySupportContracts.RequireArray(releaseScope, "deploymentProofPackageRefs").Count == 0)
        {
            return Block("SDS-000", "Release scope binding", "Deployment Proof Package refs are required.");
        }

        if (SecurityDependencySupportContracts.RequireArray(releaseScope, "includedHushVotingPaths").Count == 0)
        {
            return Block("SDS-000", "Release scope binding", "Included HushVoting paths are required.");
        }

        if (SecurityDependencySupportContracts.RequireArray(releaseScope, "lockfileRefs").Count == 0)
        {
            return Block("SDS-000", "Release scope binding", "Lockfile refs are required.");
        }

        if (SecurityDependencySupportContracts.RequireArray(releaseScope, "artifactRefs").Count == 0)
        {
            return Block("SDS-000", "Release scope binding", "Upstream artifact refs are required.");
        }

        return Pass("SDS-000", "Release scope binding", "Release scope, deployment refs, lockfiles, and artifacts are bound.");
    }

    private static SecurityDependencySupportCheck CheckScannerProvenanceCompleteness(SecurityDependencySupportSourceSet sources)
    {
        var provenanceSets = new[]
        {
            ("dependency-inventory.json", SecurityDependencySupportContracts.RequireArray(sources.DependencyInventory, "scannerProvenance")),
            ("license-scan-normalized.json", SecurityDependencySupportContracts.RequireArray(sources.LicenseScan, "scannerProvenance")),
            ("vulnerability-scan-normalized.json", SecurityDependencySupportContracts.RequireArray(sources.VulnerabilityScan, "scannerProvenance")),
        };
        var required = new[] { "toolName", "toolVersion", "runAt", "inputRefs", "rawReportHash", "normalizerVersion", "normalizedHash" };
        var missing = new List<string>();

        foreach (var (label, provenance) in provenanceSets)
        {
            if (provenance.Count == 0)
            {
                missing.Add($"{label}: scannerProvenance is empty");
                continue;
            }

            foreach (var item in provenance.OfType<JsonObject>())
            {
                missing.AddRange(required
                    .Where(field => !item.ContainsKey(field) || item[field] is null)
                    .Select(field => $"{label}: missing {field}"));
            }
        }

        return missing.Count > 0
            ? Block("SDS-001", "Scanner provenance completeness", string.Join("; ", missing))
            : Pass("SDS-001", "Scanner provenance completeness", "Scanner provenance contains tool, version, inputs, raw hash, normalizer, and normalized hash.");
    }

    private static SecurityDependencySupportCheck CheckDependencyInventoryCompleteness(SecurityDependencySupportSourceSet sources)
    {
        var components = SecurityDependencySupportContracts.RequireArray(sources.DependencyInventory, "components");
        if (components.Count == 0)
        {
            return Block("SDS-002", "Dependency inventory completeness", "No dependency components were recorded.");
        }

        var missing = new List<string>();
        foreach (var component in components.OfType<JsonObject>())
        {
            var componentId = SecurityDependencySupportContracts.GetString(component, "componentId", "unknown-component");
            if (SecurityDependencySupportContracts.RequireArray(component, "lockfileRefs").Count == 0)
            {
                missing.Add($"{componentId}: missing lockfile refs");
            }

            if (SecurityDependencySupportContracts.RequireArray(component, "dependencies").Count == 0)
            {
                missing.Add($"{componentId}: missing dependency entries");
            }
        }

        if (SecurityDependencySupportContracts.RequireArray(sources.DependencyInventory, "lockfileRefs").Count == 0)
        {
            missing.Add("inventory: missing lockfileRefs");
        }

        return missing.Count > 0
            ? Block("SDS-002", "Dependency inventory completeness", string.Join("; ", missing))
            : Pass("SDS-002", "Dependency inventory completeness", "Scoped components, dependencies, and lockfiles are present.");
    }

    private static SecurityDependencySupportCheck CheckThirdPartyLicenseGate(SecurityDependencySupportSourceSet sources)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();

        if (SecurityDependencySupportContracts.RequireArray(sources.LicenseScan, "unknownLicenses").Count > 0)
        {
            blockers.Add("unknown licenses are unresolved");
        }

        if (SecurityDependencySupportContracts.RequireArray(sources.LicenseScan, "rejectedLicenses").Count > 0)
        {
            blockers.Add("rejected licenses are present");
        }

        foreach (var finding in SecurityDependencySupportContracts.RequireArray(sources.LicenseScan, "licenseFindings").OfType<JsonObject>())
        {
            var classification = SecurityDependencySupportContracts.GetString(finding, "classification");
            var scope = SecurityDependencySupportContracts.GetString(finding, "scope");
            var license = SecurityDependencySupportContracts.GetString(finding, "license");
            var dependency = SecurityDependencySupportContracts.GetString(finding, "dependencyName", "unknown-dependency");
            var hasException = !string.IsNullOrWhiteSpace(SecurityDependencySupportContracts.GetString(finding, "exceptionRef"));
            var impactsDistributedClaim = scope is "client_runtime" or "distributed_runtime" or "server_runtime";
            var isHighReview = classification is "restricted" or "unknown" or "rejected" ||
                license.Contains("GPL", StringComparison.OrdinalIgnoreCase) ||
                license.Contains("AGPL", StringComparison.OrdinalIgnoreCase);

            if (classification is "unknown" or "rejected")
            {
                blockers.Add($"{dependency}: {classification} license classification");
                continue;
            }

            if (isHighReview && impactsDistributedClaim && !hasException)
            {
                blockers.Add($"{dependency}: restricted/high-review dependency in {scope} without exception");
            }
            else if (isHighReview && hasException)
            {
                warnings.Add($"{dependency}: restricted/high-review dependency accepted only with exception");
            }
        }

        if (blockers.Count > 0)
        {
            return Block("SDS-003", "Third-party dependency-license gate", string.Join("; ", blockers));
        }

        return warnings.Count > 0
            ? Warn("SDS-003", "Third-party dependency-license gate", string.Join("; ", warnings))
            : Pass("SDS-003", "Third-party dependency-license gate", "No rejected or unresolved third-party dependency-license blockers.");
    }

    private static SecurityDependencySupportCheck CheckVulnerabilityThresholdAndFreshness(
        SecurityDependencySupportSourceSet sources,
        DateTimeOffset generatedAt)
    {
        var blockers = new List<string>();
        var warnings = new List<string>();

        foreach (var finding in SecurityDependencySupportContracts.RequireArray(sources.VulnerabilityScan, "findings").OfType<JsonObject>())
        {
            var findingId = SecurityDependencySupportContracts.GetString(finding, "findingId", "unknown-finding");
            var severity = SecurityDependencySupportContracts.GetString(finding, "severity").ToLowerInvariant();
            var status = SecurityDependencySupportContracts.GetString(finding, "status").ToLowerInvariant();
            var hasException = !string.IsNullOrWhiteSpace(SecurityDependencySupportContracts.GetString(finding, "exceptionRef"));

            if (status == "open" && (severity is "critical" or "high") && !hasException)
            {
                blockers.Add($"{findingId}: open {severity} vulnerability");
            }
            else if (status == "open" && severity == "medium")
            {
                var owner = SecurityDependencySupportContracts.GetString(finding, "remediationOwner");
                var dueDate = SecurityDependencySupportContracts.GetString(finding, "dueDate");
                if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(dueDate))
                {
                    blockers.Add($"{findingId}: medium vulnerability missing owner or due date");
                }
                else
                {
                    warnings.Add($"{findingId}: medium vulnerability tracked with owner and due date");
                }
            }
        }

        var freshness = SecurityDependencySupportContracts.RequireObject(sources.VulnerabilityScan, "freshness");
        var producedAt = TryParseTimestamp(freshness, "producedAt");
        var maxAgeDays = freshness.TryGetPropertyValue("maxAgeDays", out var maxAgeNode) && maxAgeNode is not null
            ? maxAgeNode.GetValue<int>()
            : 30;
        if (producedAt is null)
        {
            blockers.Add("vulnerability freshness producedAt is missing or invalid");
        }
        else if (generatedAt - producedAt.Value > TimeSpan.FromDays(maxAgeDays))
        {
            blockers.Add($"vulnerability scan is older than {maxAgeDays} days");
        }

        if (blockers.Count > 0)
        {
            return Block("SDS-004", "Vulnerability threshold and freshness gate", string.Join("; ", blockers));
        }

        return warnings.Count > 0
            ? Warn("SDS-004", "Vulnerability threshold and freshness gate", string.Join("; ", warnings))
            : Pass("SDS-004", "Vulnerability threshold and freshness gate", "No critical/high blockers and scan freshness is inside policy.");
    }

    private static SecurityDependencySupportCheck CheckDisclosureProcessGate(SecurityDependencySupportSourceSet sources)
    {
        var missing = new[]
        {
            "intakeChannel",
            "triageOwner",
            "severityRubric",
            "embargoRule",
            "customerNotificationRule",
            "publicPrivateBoundary",
        }.Where(field => !sources.DisclosureProcess.ContainsKey(field) || sources.DisclosureProcess[field] is null).ToArray();

        if (missing.Length > 0)
        {
            return Block("SDS-005", "Disclosure process gate", $"Disclosure process is missing {string.Join(", ", missing)}.");
        }

        if (!SecurityDependencySupportContracts.GetBool(sources.DisclosureProcess, "concretePilotChannelConfigured"))
        {
            return Warn("SDS-005", "Disclosure process gate", "security-intake-private-v1 placeholder is defined, but concrete pilot channel is not configured yet.");
        }

        return Pass("SDS-005", "Disclosure process gate", "Private intake, triage, embargo, notification, and public/private boundary are defined.");
    }

    private static SecurityDependencySupportCheck CheckAccessibilityEvidenceGate(SecurityDependencySupportSourceSet sources)
    {
        if (SecurityDependencySupportContracts.RequireArray(sources.AccessibilityEvidence, "feat103EvidenceRefs").Count == 0)
        {
            return Block("SDS-006", "Accessibility evidence gate", "Accessibility evidence refs are required.");
        }

        if (SecurityDependencySupportContracts.RequireArray(sources.AccessibilityEvidence, "blockingWorkflows").Count > 0)
        {
            return Block("SDS-006", "Accessibility evidence gate", "Accessibility blocking workflows are present.");
        }

        if (SecurityDependencySupportContracts.RequireArray(sources.AccessibilityEvidence, "missingCoverage").Count > 0)
        {
            return Block("SDS-006", "Accessibility evidence gate", "Accessibility coverage is missing for claim-bearing workflows.");
        }

        if (SecurityDependencySupportContracts.RequireArray(sources.AccessibilityEvidence, "staleCoverage").Count > 0)
        {
            return Warn("SDS-006", "Accessibility evidence gate", "Some accessibility evidence is stale and must be refreshed before stronger claims.");
        }

        return Pass("SDS-006", "Accessibility evidence gate", "Accessibility refs cover claim-bearing workflows.");
    }

    private static SecurityDependencySupportCheck CheckVoterClientIntegrityGate(SecurityDependencySupportSourceSet sources)
    {
        var claimBearingPaths = SecurityDependencySupportContracts.GetStringSet(sources.VoterClientGuidance, "claimBearingPaths");
        var supportedPaths = SecurityDependencySupportContracts.GetStringSet(sources.VoterClientGuidance, "supportedPaths");
        if (!claimBearingPaths.Contains("desktop_web") || !claimBearingPaths.Contains("mobile_web"))
        {
            return Block("SDS-007", "Voter-client integrity and mobile web gate", "Desktop web and mobile web must both be claim-bearing paths.");
        }

        if (!supportedPaths.Contains("desktop_web") || !supportedPaths.Contains("mobile_web"))
        {
            return Block("SDS-007", "Voter-client integrity and mobile web gate", "Desktop web and mobile web must both be supported for v1 claims.");
        }

        if (SecurityDependencySupportContracts.RequireArray(sources.VoterClientGuidance, "mobileEvidenceRefs").Count == 0)
        {
            return Block("SDS-007", "Voter-client integrity and mobile web gate", "Mobile web requires current mobile platform and accessibility evidence refs.");
        }

        var notAvailableV1 = SecurityDependencySupportContracts.RequireArray(sources.VoterClientGuidance, "notAvailableV1Paths")
            .OfType<JsonObject>()
            .Select(node => SecurityDependencySupportContracts.GetString(node, "path"))
            .ToArray();
        return notAvailableV1.Length > 0
            ? Warn("SDS-007", "Voter-client integrity and mobile web gate", $"Explicit not_available_v1 paths remain: {string.Join(", ", notAvailableV1)}.")
            : Pass("SDS-007", "Voter-client integrity and mobile web gate", "Desktop and mobile web evidence is available.");
    }

    private static SecurityDependencySupportCheck CheckSupportReadinessGate(SecurityDependencySupportSourceSet sources)
    {
        if (SecurityDependencySupportContracts.RequireArray(sources.SupportReadiness, "missingRunbooks").Count > 0)
        {
            return Block("SDS-008", "Support readiness gate", "Support readiness declares missing runbooks.");
        }

        var runbooks = SecurityDependencySupportContracts.RequireArray(sources.SupportReadiness, "runbookRefs")
            .OfType<JsonValue>()
            .Select(value => value.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        var missingRequiredRunbooks = SecurityDependencySupportContracts.RequiredSupportRunbooks
            .Where(runbook => !runbooks.Contains(runbook))
            .ToArray();

        return missingRequiredRunbooks.Length > 0
            ? Block("SDS-008", "Support readiness gate", $"Missing required support runbooks: {string.Join(", ", missingRequiredRunbooks)}.")
            : Pass("SDS-008", "Support readiness gate", "Required support categories, runbooks, escalation refs, and privacy rules are present.");
    }

    private static SecurityDependencySupportCheck CheckSupportExportPrivacyGate(SecurityDependencySupportSourceSet sources)
    {
        if (SecurityDependencySupportContracts.GetString(sources.SupportExportPrivacyProof, "privacyResult") != "passed")
        {
            return Block("SDS-009", "Support-export privacy gate", "Support export privacy result is not passed.");
        }

        var scanResults = SecurityDependencySupportContracts.RequireArray(sources.SupportExportPrivacyProof, "scanResults");
        foreach (var result in scanResults.OfType<JsonObject>())
        {
            if (SecurityDependencySupportContracts.RequireArray(result, "forbiddenFindings").Count > 0 ||
                SecurityDependencySupportContracts.GetString(result, "result") != "passed")
            {
                return Block("SDS-009", "Support-export privacy gate", "Support export fixture scan found forbidden material.");
            }
        }

        var forbidden = SecurityDependencySupportContracts.ScanForbiddenMaterial(
            sources.SupportExportPrivacyProof,
            "restricted",
            "support-export-privacy-proof.json");
        return forbidden.Count > 0
            ? Block("SDS-009", "Support-export privacy gate", "Support export privacy proof contains forbidden private material.")
            : Pass("SDS-009", "Support-export privacy gate", "Support export proof and fixture scans do not expose forbidden private material.");
    }

    private static SecurityDependencySupportCheck CheckExceptionHandoffAndClaimImpactGate(SecurityDependencySupportSourceSet sources)
    {
        var exceptionRecords = SecurityDependencySupportContracts.RequireArray(sources.Exceptions, "exceptions").OfType<JsonObject>().ToArray();
        var exceptionIds = exceptionRecords
            .Select(record => SecurityDependencySupportContracts.GetString(record, "exceptionId"))
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);
        var packageExceptionRefs = SecurityDependencySupportContracts.RequireArray(sources.Package, "exceptionRefs")
            .OfType<JsonValue>()
            .Select(value => value.GetValue<string>())
            .ToArray();
        var missing = packageExceptionRefs.Where(reference => !exceptionIds.Contains(reference)).ToArray();
        if (missing.Length > 0)
        {
            return Block("SDS-010", "Exception, handoff, and claim impact gate", $"Missing exception records: {string.Join(", ", missing)}.");
        }

        var required = new[]
        {
            "exceptionId",
            "sourceGap",
            "acceptanceGate",
            "affectedClaim",
            "ownerSignoff",
            "reason",
            "reviewDate",
            "expiryDate",
            "compensatingEvidence",
            "scoreImpact",
            "claimWordingImpact",
            "action",
        };
        var malformed = exceptionRecords
            .SelectMany(record => required
                .Where(field => !record.ContainsKey(field) || record[field] is null)
                .Select(field => $"{SecurityDependencySupportContracts.GetString(record, "exceptionId", "unknown-exception")}: missing {field}"))
            .ToArray();
        if (malformed.Length > 0)
        {
            return Block("SDS-010", "Exception, handoff, and claim impact gate", string.Join("; ", malformed));
        }

        if (!sources.Package.ContainsKey("claimImpact"))
        {
            return Block("SDS-010", "Exception, handoff, and claim impact gate", "Package claimImpact is required for downstream handoff.");
        }

        return exceptionRecords.Length > 0
            ? Warn("SDS-010", "Exception, handoff, and claim impact gate", "Exceptions are well-formed and must remain visible to downstream readiness and pilot packaging.")
            : Pass("SDS-010", "Exception, handoff, and claim impact gate", "No exceptions are required and claim impact is present.");
    }

    private static SecurityDependencySupportCheck Pass(string id, string name, string reason) =>
        new(id, name, "passed", reason);

    private static SecurityDependencySupportCheck Warn(string id, string name, string reason) =>
        new(id, name, "warning", reason);

    private static SecurityDependencySupportCheck Block(string id, string name, string reason) =>
        new(id, name, "blocked", reason);

    private static DateTimeOffset? TryParseTimestamp(JsonObject value, string property)
    {
        var text = SecurityDependencySupportContracts.GetString(value, property);
        return DateTimeOffset.TryParse(text, out var parsed)
            ? parsed.ToUniversalTime()
            : null;
    }
}
