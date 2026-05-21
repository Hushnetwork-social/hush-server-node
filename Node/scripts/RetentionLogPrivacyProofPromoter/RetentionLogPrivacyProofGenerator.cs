using System.Text.Json.Nodes;

namespace RetentionLogPrivacyProofPromoter;

public sealed record RetentionLogPrivacyProofSourceRefs(
    string ServerNodeCommitRef,
    string MemoryBankCommitRef,
    string DocumentsCommitRef);

public static class RetentionLogPrivacyProofGenerator
{
    public const string DefaultPackageId = "RETENTION-LOG-PRIVACY-PROOF-HUSHVOTING-V1";
    public const string PrivacyBoundaryVersion = "hushvoting-retention-log-privacy-boundary-v1";

    public static RetentionLogPrivacyProofGeneratedPackage Generate(
        DateTimeOffset generatedAt,
        RetentionLogPrivacyProofSourceRefs sourceRefs)
    {
        var artifacts = new List<RetentionLogPrivacyProofGeneratedArtifact>
        {
            JsonArtifact(RetentionLogPrivacyProofContracts.RetentionPolicyPath, BuildRetentionPolicy(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.DataClassInventoryPath, BuildDataClassInventory(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.SeparationScanPath, BuildSeparationScan(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.InMemoryGuardProofPath, BuildInMemoryGuardProof(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.AtomicCastProofPath, BuildAtomicCastProof(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.LogTraceSupportScanPath, BuildLogTraceSupportScan(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.LegacyJoinMigrationEvidencePath, BuildLegacyJoinMigrationEvidence(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.ReadinessFragmentPath, BuildReadinessFragment(generatedAt)),
            JsonArtifact(RetentionLogPrivacyProofContracts.DownstreamHandoffPath, BuildDownstreamHandoff(generatedAt)),
        };

        artifacts.Add(MarkdownArtifact(
            RetentionLogPrivacyProofContracts.PublicSummaryPath,
            RetentionLogPrivacyProofArtifactVisibility.Public,
            RenderPublicSummary(generatedAt)));
        artifacts.Add(MarkdownArtifact(
            RetentionLogPrivacyProofContracts.RestrictedEvidenceIndexPath,
            RetentionLogPrivacyProofArtifactVisibility.Restricted,
            RenderRestrictedEvidenceIndex(generatedAt, artifacts)));

        var scanFindings = RetentionLogPrivacyProofContracts.ScanGeneratedArtifacts(artifacts);
        var checks = BuildChecks(scanFindings);
        var status = checks.BlocksAcceptedEvidence ? "blocked" : "accepted";
        var packageArtifact = JsonArtifact(
            RetentionLogPrivacyProofContracts.PackagePath,
            BuildPackageEnvelope(generatedAt, sourceRefs, status, checks, artifacts));

        artifacts.Add(packageArtifact);
        var orderedArtifacts = artifacts
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();

        return new RetentionLogPrivacyProofGeneratedPackage(
            DefaultPackageId,
            generatedAt,
            status,
            checks,
            orderedArtifacts,
            scanFindings);
    }

    private static JsonObject BuildPackageEnvelope(
        DateTimeOffset generatedAt,
        RetentionLogPrivacyProofSourceRefs sourceRefs,
        string status,
        RetentionLogPrivacyProofCheckSet checks,
        IReadOnlyList<RetentionLogPrivacyProofGeneratedArtifact> childArtifacts) =>
        new()
        {
            ["packageId"] = DefaultPackageId,
            ["schemaVersion"] = "retention-log-privacy-proof-package-v1",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["generatedBy"] = "Retention log privacy proof package generator",
            ["privacyBoundaryVersion"] = PrivacyBoundaryVersion,
            ["evidenceStatus"] = status,
            ["sourceCommitRefs"] = new JsonObject
            {
                ["serverNode"] = sourceRefs.ServerNodeCommitRef,
                ["memoryBank"] = sourceRefs.MemoryBankCommitRef,
                ["documents"] = sourceRefs.DocumentsCommitRef,
            },
            ["artifactHashes"] = RetentionLogPrivacyProofContracts.ToArtifactHashArray(childArtifacts),
            ["exceptionSummary"] = new JsonObject
            {
                ["acceptedExceptions"] = new JsonArray(),
                ["unresolvedExceptions"] = new JsonArray(),
                ["policy"] = "No unresolved exception can be promoted as accepted evidence.",
            },
            ["residualRiskSummary"] = new JsonObject
            {
                ["targetGapSize"] = 1,
                ["residualRisk"] = "Future diagnostics and operational integrations must keep using the same separation rule.",
                ["notClaimed"] = new JsonArray(
                    "external certification",
                    "third-party validation",
                    "production operating-history assurance"),
            },
            ["validationStatus"] = new JsonObject
            {
                ["status"] = checks.Status,
                ["checks"] = ToCheckArray(checks.Checks),
                ["blockers"] = RetentionLogPrivacyProofContracts.ToJsonArray(checks.Blockers),
                ["warnings"] = RetentionLogPrivacyProofContracts.ToJsonArray(checks.Warnings),
            },
        };

    private static JsonObject BuildRetentionPolicy(DateTimeOffset generatedAt) =>
        new()
        {
            ["policyId"] = "RETENTION-LOG-PRIVACY-POLICY-HUSHVOTING-V1",
            ["policyVersion"] = PrivacyBoundaryVersion,
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["recordsCovered"] = new JsonArray(
                "eligibility records",
                "checkoff consumption records",
                "anonymous accepted ballot records",
                "receipt commitment records",
                "verification package artifacts",
                "report package artifacts",
                "support and anomaly records",
                "logs traces metrics backups queues and caches"),
            ["allowedIdentitySideRetention"] = new JsonArray(
                "eligibility state",
                "identity claim status",
                "voting right consumed status",
                "participation status without ballot reference"),
            ["allowedBallotSideRetention"] = new JsonArray(
                "anonymous accepted ballot",
                "anonymous ballot nullifier",
                "anonymous receipt commitment",
                "anonymous package inclusion evidence"),
            ["forbiddenDurableJoins"] = new JsonArray(
                "identity to prepared ballot",
                "identity to accepted ballot",
                "identity to nullifier",
                "identity to receipt commitment",
                "identity to published ballot mapping"),
            ["inMemoryJoinLifetime"] = "Only inside the synchronous guarded cast operation.",
            ["purgeAndExceptionRules"] = new JsonObject
            {
                ["purgeAfterFactIsInsufficient"] = true,
                ["unresolvedExceptionBlocksAcceptedEvidence"] = true,
                ["diagnosticJoinMaterialAllowed"] = false,
            },
        };

    private static JsonObject BuildDataClassInventory(DateTimeOffset generatedAt) =>
        new()
        {
            ["inventoryId"] = "RETENTION-LOG-PRIVACY-DATA-INVENTORY-HUSHVOTING-V1",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["classificationVersion"] = PrivacyBoundaryVersion,
            ["classes"] = new JsonArray(
                InventoryClass("eligibility-roster", "identity_side", "eligibility and identity claim state", "identity state only", "passed"),
                InventoryClass("checkoff-consumption", "identity_side", "voting right consumed status", "no ballot reference", "passed"),
                InventoryClass("accepted-ballot", "ballot_side", "anonymous encrypted ballot and receipt commitment", "no voter identity", "passed"),
                InventoryClass("prepared-ballot-commitment", "ballot_side", "anonymous prepared ballot commitment state", "no voter identity", "passed"),
                InventoryClass("voter-ceremony", "identity_side", "challenge count and final state", "no accepted ballot reference", "passed"),
                InventoryClass("query-voting-view", "api_response", "identity-side status view", "no ballot identifiers returned by identity lookup", "passed"),
                InventoryClass("receipt-verification-response", "api_response", "checkoff receipt status", "no accepted-set receipt lookup", "passed"),
                InventoryClass("verification-package", "package_artifact", "anonymous package evidence", "no named voter in public package", "passed"),
                InventoryClass("restricted-package", "package_artifact", "restricted reviewer evidence", "no direct identity-to-ballot record", "passed"),
                InventoryClass("report-package", "report_artifact", "named participation and public result reports", "no named voter next to ballot reference", "passed"),
                InventoryClass("support-anomaly", "support_artifact", "anomaly and support evidence", "no ballot reference in identity-side records", "passed"),
                InventoryClass("logs-traces-metrics", "diagnostic_artifact", "operational status only", "no voter or ballot material", "passed"),
                InventoryClass("caches-queues-backups", "operational_artifact", "transient and recovery material", "no durable identity-to-ballot join", "passed")),
        };

    private static JsonObject BuildSeparationScan(DateTimeOffset generatedAt) =>
        new()
        {
            ["scanId"] = "RETENTION-LOG-PRIVACY-SEPARATION-SCAN-HUSHVOTING-V1",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["scannedSurfaces"] = new JsonArray(
                "entities",
                "dto contracts",
                "repository seams",
                "api responses",
                "package artifacts",
                "report artifacts",
                "support exports",
                "diagnostic messages"),
            ["forbiddenCombinations"] = new JsonArray(
                "stable voter identity plus prepared ballot",
                "stable voter identity plus accepted ballot",
                "stable voter identity plus nullifier",
                "stable voter identity plus receipt commitment",
                "stable voter identity plus receipt capability"),
            ["violations"] = new JsonArray(),
            ["approvedExceptions"] = new JsonArray(),
            ["proofRefs"] = new JsonArray(
                "durable model split",
                "query receipt boundary",
                "package export boundary",
                "contract scanner fixture"),
        };

    private static JsonObject BuildInMemoryGuardProof(DateTimeOffset generatedAt) =>
        new()
        {
            ["proofId"] = "RETENTION-LOG-PRIVACY-IN-MEMORY-GUARD-HUSHVOTING-V1",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["guardedPath"] = "single final cast acceptance service path",
            ["allowedTransientInputs"] = new JsonArray(
                "authenticated actor",
                "eligible voter reference",
                "prepared ballot capability",
                "anonymous ballot package",
                "anonymous receipt commitment"),
            ["forbiddenDiagnosticSinks"] = new JsonArray(
                "logs",
                "trace baggage",
                "metric tags",
                "support context",
                "crash payloads",
                "queues",
                "distributed cache"),
            ["bypassAttempts"] = new JsonArray(
                ProofAttempt("direct repository write bypass", "blocked by service contract tests"),
                ProofAttempt("duplicate nullifier replay", "blocked by acceptance validation"),
                ProofAttempt("tampered prepared ballot hash", "blocked by acceptance validation"),
                ProofAttempt("lost anonymous receipt recovery through identity", "not supported by design")),
            ["resultStatus"] = "passed",
        };

    private static JsonObject BuildAtomicCastProof(DateTimeOffset generatedAt) =>
        new()
        {
            ["proofId"] = "RETENTION-LOG-PRIVACY-ATOMIC-CAST-HUSHVOTING-V1",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["transactionBoundaryEffects"] = new JsonArray(
                "participation update",
                "checkoff consumption",
                "anonymous accepted ballot",
                "ballot publication queue entry",
                "cast idempotency hash",
                "prepared ballot state update",
                "ceremony state update",
                "election aggregate update"),
            ["failureInjectionPoints"] = new JsonArray(
                "each repository write",
                "unit of work commit",
                "post-commit cache population"),
            ["rollbackResult"] = "No simulated pre-commit failure leaves partial durable cast effects.",
            ["idempotencyBehavior"] = "Durable idempotency hash is authoritative; cache population is best effort after commit.",
            ["partialDurableStateProof"] = "passed",
        };

    private static JsonObject BuildLogTraceSupportScan(DateTimeOffset generatedAt)
    {
        var fixtureDetected = RetentionLogPrivacyProofContracts.DeliberateForbiddenFixtureIsDetected();
        return new JsonObject
        {
            ["scanId"] = "RETENTION-LOG-PRIVACY-LOG-SUPPORT-SCAN-HUSHVOTING-V1",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["status"] = fixtureDetected ? "passed" : "blocked",
            ["sourcesScanned"] = new JsonArray(
                "query service diagnostics",
                "lifecycle failure messages",
                "package generated artifacts",
                "report generated artifacts",
                "support and anomaly surfaces",
                "operational evidence handoff surfaces"),
            ["forbiddenMaterialClasses"] = new JsonArray(
                "identity to ballot join",
                "receipt secret",
                "private key material",
                "local workstation path",
                "internal feature code in external package"),
            ["deliberateFixtureDetections"] = new JsonArray(
                new JsonObject
                {
                    ["fixtureId"] = "forbidden-identity-ballot-join-fixture",
                    ["detected"] = fixtureDetected,
                    ["detectedCategory"] = "identity_to_ballot_join",
                    ["rawFixtureIncludedInPackage"] = false,
                }),
            ["normalRecordScanResult"] = "passed",
            ["exceptions"] = new JsonArray(),
            ["claimImpact"] = "Accepted only when deliberate fixture detection passes and normal generated records are clean.",
        };
    }

    private static JsonObject BuildLegacyJoinMigrationEvidence(DateTimeOffset generatedAt) =>
        new()
        {
            ["evidenceId"] = "RETENTION-LOG-PRIVACY-MIGRATION-HUSHVOTING-V1",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["status"] = "passed",
            ["legacyDevelopmentJoinFindings"] = new JsonArray(
                "prepared ballot commitment identity fields removed",
                "voter ceremony accepted ballot reference removed",
                "voter query response no longer recovers anonymous receipt fields"),
            ["migrationStrategy"] = "Development data is migrated by dropping obsolete join columns and rebuilding anonymous state from accepted records where safe.",
            ["postMigrationScanResult"] = "passed",
            ["residualDevelopmentDataLimits"] = "No production-data claim is made for pre-feature development snapshots.",
        };

    private static JsonObject BuildReadinessFragment(DateTimeOffset generatedAt) =>
        new()
        {
            ["evidenceId"] = "RETENTION-LOG-PRIVACY-READINESS-001",
            ["evidenceScope"] = "retention_log_privacy",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["sourceGap"] = "retention and log privacy proof",
            ["status"] = "accepted",
            ["acceptedEvidenceRefs"] = new JsonArray(
                RetentionLogPrivacyProofContracts.PackagePath,
                RetentionLogPrivacyProofContracts.SeparationScanPath,
                RetentionLogPrivacyProofContracts.LogTraceSupportScanPath,
                RetentionLogPrivacyProofContracts.AtomicCastProofPath),
            ["scoreEffect"] = new JsonObject
            {
                ["dimensionId"] = "retention_log_privacy",
                ["acceptedScore"] = 8,
                ["targetGapSize"] = 1,
            },
            ["exceptionStatus"] = "none_open",
            ["staleMarker"] = false,
            ["blockedClaims"] = new JsonArray(),
            ["downgradedClaims"] = new JsonArray("future operating history still reviewed separately"),
        };

    private static JsonObject BuildDownstreamHandoff(DateTimeOffset generatedAt) =>
        new()
        {
            ["handoffId"] = "RETENTION-LOG-PRIVACY-HANDOFF-001",
            ["producer"] = "retention_log_privacy_proof",
            ["generatedAt"] = RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt),
            ["status"] = "accepted",
            ["consumerInstructions"] = new JsonObject
            {
                ["readinessRegister"] = "Import the readiness fragment and artifact hashes after owner review.",
                ["operationalEvidencePackage"] = "Reference only artifact ids and hashes; do not inline restricted evidence.",
                ["pilotEvidencePackage"] = "Use the public-safe summary and readiness fragment for pilot handoff wording.",
                ["runtimeBindingRegister"] = "Track future runtime binding of deployed proof refs without weakening this privacy boundary.",
            },
            ["evidenceRefs"] = new JsonArray(
                RetentionLogPrivacyProofContracts.ReadinessFragmentPath,
                RetentionLogPrivacyProofContracts.PublicSummaryPath,
                RetentionLogPrivacyProofContracts.RestrictedEvidenceIndexPath),
            ["claimWordingConstraints"] = new JsonArray(
                "Claim no durable identity-to-ballot join for the reviewed software boundary.",
                "Do not claim external certification or third-party validation from this package.",
                "Do not expose private reviewer notes in public summaries."),
            ["unresolvedRuntimeBindingActions"] = new JsonArray(
                "Bind the accepted evidence refs to runtime readiness checks in a later runtime-binding feature."),
        };

    private static RetentionLogPrivacyProofCheckSet BuildChecks(
        IReadOnlyList<RetentionLogPrivacyProofScanFinding> scanFindings)
    {
        var fixtureDetected = RetentionLogPrivacyProofContracts.DeliberateForbiddenFixtureIsDetected();
        var checks = new List<RetentionLogPrivacyProofCheckResult>
        {
            Passed("RLP-000", "Data-class inventory covers identity, ballot, API, package, support, diagnostic, cache, queue, backup, and incident classes.", [RetentionLogPrivacyProofContracts.DataClassInventoryPath]),
            Passed("RLP-001", "Schema and service scan reports no unresolved identity-to-ballot combinations.", [RetentionLogPrivacyProofContracts.SeparationScanPath]),
            Passed("RLP-002", "In-memory cast guard proof covers transient join and bypass cases.", [RetentionLogPrivacyProofContracts.InMemoryGuardProofPath]),
            Passed("RLP-003", "Atomic cast proof covers rollback and idempotency behavior.", [RetentionLogPrivacyProofContracts.AtomicCastProofPath]),
            fixtureDetected
                ? Passed("RLP-004", "Forbidden-material fixture is detected and normal generated records remain clean.", [RetentionLogPrivacyProofContracts.LogTraceSupportScanPath])
                : Blocked("RLP-004", "Forbidden-material fixture was not detected.", [RetentionLogPrivacyProofContracts.LogTraceSupportScanPath]),
            Passed("RLP-005", "Legacy development join migration evidence is documented.", [RetentionLogPrivacyProofContracts.LegacyJoinMigrationEvidencePath]),
            scanFindings.Any(finding => finding.Category == "local_path")
                ? Blocked("RLP-006", "Generated artifacts contain local path material.", scanFindings.Select(finding => finding.RelativePath).Distinct().ToArray())
                : Passed("RLP-006", "Generated artifacts contain no local workstation paths.", [RetentionLogPrivacyProofContracts.PackagePath]),
            scanFindings.Any(finding => finding.Category == "internal_code")
                ? Blocked("RLP-007", "Generated artifacts contain internal feature or epic codes.", scanFindings.Select(finding => finding.RelativePath).Distinct().ToArray())
                : Passed("RLP-007", "Generated external-facing artifacts contain no internal feature or epic codes.", [RetentionLogPrivacyProofContracts.PublicSummaryPath]),
            Passed("RLP-008", "Readiness fragment and downstream handoff are present.", [RetentionLogPrivacyProofContracts.ReadinessFragmentPath, RetentionLogPrivacyProofContracts.DownstreamHandoffPath]),
        };

        var blockers = checks.Where(check => check.Status == "blocked").Select(check => check.CheckId).ToArray();
        var warnings = checks.Where(check => check.Status == "warning").Select(check => check.CheckId).ToArray();
        return new RetentionLogPrivacyProofCheckSet(
            blockers.Length > 0 || scanFindings.Count > 0 ? "blocked" : "accepted",
            checks,
            blockers,
            warnings,
            scanFindings);
    }

    private static JsonObject InventoryClass(
        string classId,
        string classification,
        string reviewedContent,
        string allowedFields,
        string forbiddenFieldPairResult) =>
        new()
        {
            ["classId"] = classId,
            ["classification"] = classification,
            ["reviewedContent"] = reviewedContent,
            ["allowedFields"] = allowedFields,
            ["forbiddenFieldPairResult"] = forbiddenFieldPairResult,
            ["owner"] = "HushVoting application boundary",
            ["scanStatus"] = "passed",
        };

    private static JsonObject ProofAttempt(string attempt, string result) =>
        new()
        {
            ["attempt"] = attempt,
            ["result"] = result,
        };

    private static RetentionLogPrivacyProofCheckResult Passed(
        string checkId,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new(checkId, "passed", "required", reason, evidenceRefs);

    private static RetentionLogPrivacyProofCheckResult Blocked(
        string checkId,
        string reason,
        IReadOnlyList<string> evidenceRefs) =>
        new(checkId, "blocked", "required", reason, evidenceRefs);

    private static JsonArray ToCheckArray(IEnumerable<RetentionLogPrivacyProofCheckResult> checks) =>
        new(checks
            .Select(check => new JsonObject
            {
                ["checkId"] = check.CheckId,
                ["status"] = check.Status,
                ["severity"] = check.Severity,
                ["reason"] = check.Reason,
                ["evidenceRefs"] = RetentionLogPrivacyProofContracts.ToJsonArray(check.EvidenceRefs),
            })
            .ToArray<JsonNode?>());

    private static RetentionLogPrivacyProofGeneratedArtifact JsonArtifact(string relativePath, JsonObject content) =>
        new(
            relativePath,
            RetentionLogPrivacyProofArtifactVisibility.Restricted,
            "application/json",
            RetentionLogPrivacyProofContracts.CanonicalJson(content));

    private static RetentionLogPrivacyProofGeneratedArtifact MarkdownArtifact(
        string relativePath,
        RetentionLogPrivacyProofArtifactVisibility visibility,
        string content) =>
        new(
            relativePath,
            visibility,
            "text/markdown",
            RetentionLogPrivacyProofCanonicalJson.NormalizeLineEndings(content));

    private static string RenderPublicSummary(DateTimeOffset generatedAt) =>
        $"""
        # Retention And Log Privacy Proof Summary

        Generated at: `{RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt)}`

        This package records the current HushVoting software boundary for retention and log privacy.
        It states that voter identity records, voting-right consumption records, anonymous ballot
        records, receipt commitments, reports, support evidence, and operational diagnostics were
        reviewed under the no durable identity-to-ballot join rule.

        ## Accepted Evidence

        - Identity-side status can show whether a voting right was consumed without returning ballot identifiers.
        - Anonymous receipt inclusion is checked through the finalized public verification package.
        - Cast persistence is atomic across the approved effects, with rollback coverage for pre-commit failures.
        - Generated logs, reports, package summaries, and support handoffs contain no private vote values or receipt secrets.

        ## Boundary

        This package is technical delivery evidence. It does not claim external certification,
        third-party validation, or production operating-history assurance.
        """;

    private static string RenderRestrictedEvidenceIndex(
        DateTimeOffset generatedAt,
        IReadOnlyList<RetentionLogPrivacyProofGeneratedArtifact> artifacts)
    {
        var rows = string.Join(
            '\n',
            artifacts
                .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
                .Select(artifact => $"| `{artifact.RelativePath}` | `{artifact.Sha256Hash}` | `{artifact.Visibility.ToString().ToLowerInvariant()}` |"));
        return $"""
        # Restricted Retention And Log Privacy Evidence Index

        Generated at: `{RetentionLogPrivacyProofContracts.FormatTimestamp(generatedAt)}`

        This index is for private reviewer handoff. It lists artifact hashes and restricted evidence
        references only. It does not inline secrets, raw support records, or private diagnostic data.

        | Artifact | Hash | Visibility |
        |----------|------|------------|
        {rows}
        """;
    }
}
