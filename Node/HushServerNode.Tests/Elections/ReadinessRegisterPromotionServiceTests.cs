using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ReadinessRegisterPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ReadinessRegisterPromotionServiceTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = new(2026, 5, 25, 15, 45, 0, TimeSpan.Zero);
    private const string CurrentRegisterVersion = "v0.1.3";
    private const string CurrentRegisterVersionId = "RDY-REG-v0.1.3";
    private const int CurrentTotalScore = 60;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    [Fact]
    public void Promote_WithBaselineRegister_ProducesStableManifestArchiveAndCatalog()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        var options = CreateOptions(paths);
        var service = new ReadinessRegisterPromotionService();

        var first = service.Promote(options);
        var second = service.Promote(options);

        second.ManifestHash.Should().Be(first.ManifestHash);
        second.ArchiveHash.Should().Be(first.ArchiveHash);
        second.TotalScore.Should().Be(CurrentTotalScore);
        second.Status.Should().Be("AcceptedInternal");
        File.Exists(Path.Combine(first.VersionOutputRoot, ReadinessRegisterPromotionService.ManifestFileName)).Should().BeTrue();
        File.Exists(Path.Combine(first.VersionOutputRoot, $"HushVoting-Readiness-Register-{CurrentRegisterVersion}.zip")).Should().BeTrue();
        File.Exists(paths.CatalogPath).Should().BeTrue();

        var catalog = JsonNode.Parse(File.ReadAllText(paths.CatalogPath))!.AsObject();
        catalog["currentRegisterVersionId"]!.GetValue<string>().Should().Be(CurrentRegisterVersionId);
        catalog["currentManifestHash"]!.GetValue<string>().Should().Be(first.ManifestHash);
        catalog["currentArchiveHash"]!.GetValue<string>().Should().Be(first.ArchiveHash);
    }

    [Fact]
    public void Promote_WithValidateOnly_DoesNotWriteOutput()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        var options = CreateOptions(paths, validateOnly: true);

        var result = new ReadinessRegisterPromotionService().Promote(options);

        result.RegisterVersionId.Should().Be(CurrentRegisterVersionId);
        Directory.Exists(result.VersionOutputRoot).Should().BeFalse();
        File.Exists(paths.CatalogPath).Should().BeFalse();
    }

    [Fact]
    public void Promote_WithNoPublicationStatusOverride_PreservesRegisterPublicationStatus()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegister(paths, register =>
        {
            register["generatedViews"]!.AsObject()["publicSafePublicationStatus"] = "pilot_only_with_limitations";
        });

        var result = new ReadinessRegisterPromotionService().Promote(CreateOptions(
            paths,
            publicationStatus: null));

        result.PublicationStatus.Should().Be("pilot_only_with_limitations");
    }

    [Fact]
    public void Promote_WithFeat147Source_WritesAuditPackageAndCheckOnlyVerifies()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        WriteFeat147PromotionSource(paths);
        var service = new ReadinessRegisterPromotionService();

        service.Promote(CreateOptions(paths));

        var auditRoot = GetFeat147AuditPackageRoot(paths);
        File.Exists(Path.Combine(auditRoot, "feat147-blocker-resolution-decision-ledger.json")).Should().BeTrue();
        File.Exists(Path.Combine(auditRoot, "feat147-artifact-hash-audit.json")).Should().BeTrue();
        File.Exists(Path.Combine(auditRoot, "feat147-promotion-hash-validation.json")).Should().BeTrue();
        var audit = JsonNode.Parse(File.ReadAllText(Path.Combine(auditRoot, "feat147-artifact-hash-audit.json")))!.AsObject();
        audit["status"]!.GetValue<string>().Should().Be("passed");

        var checkOnly = service.Promote(CreateOptions(paths, checkOnly: true));
        checkOnly.RegisterVersionId.Should().Be(CurrentRegisterVersionId);

        File.AppendAllText(
            Path.Combine(auditRoot, "feat147-promotion-decision-summary.md"),
            "tampered");

        var act = () => service.Promote(CreateOptions(paths, checkOnly: true));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("FEAT-147 audit artifact mismatch", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithFeat147ResolvingDecisionAndFailedArtifactAudit_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        WriteFeat147PromotionSource(paths, forceBadArtifactHash: true);

        var act = () => new ReadinessRegisterPromotionService().Promote(CreateOptions(paths));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Message.Contains("artifact audit failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Promote_WithFeat150Source_WritesCleanupPackageAndCheckOnlyVerifies()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForFeat150Cleanup(paths);
        WriteFeat150CleanupSource(paths);
        var service = new ReadinessRegisterPromotionService();

        var result = service.Promote(CreateOptions(paths));

        var cleanupRoot = GetFeat150CleanupPackageRoot(paths);
        File.Exists(Path.Combine(cleanupRoot, "feat150-blocker-cleanup-decision-ledger.json")).Should().BeTrue();
        File.Exists(Path.Combine(cleanupRoot, "feat150-generated-view-consistency-check.json")).Should().BeTrue();
        File.Exists(Path.Combine(cleanupRoot, "feat150-public-safe-scan.json")).Should().BeTrue();
        File.Exists(Path.Combine(cleanupRoot, "feat150-artifact-hash-audit.json")).Should().BeTrue();
        var scorecard = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.ScorecardFileName));
        scorecard.Should().Contain("Current strongest allowed claim: friendly_organization_pilot");
        scorecard.Should().Contain("Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; production rollout and public/state election readiness remain blocked.");
        scorecard.Should().NotContain("Current go/no-go result: internal non-binding rehearsal is allowed with limitations; pilot and stronger claims are blocked.");

        var checkOnly = service.Promote(CreateOptions(paths, checkOnly: true));
        checkOnly.RegisterVersionId.Should().Be(CurrentRegisterVersionId);

        File.AppendAllText(
            Path.Combine(cleanupRoot, "feat150-cleanup-decision-summary.md"),
            "tampered");

        var act = () => service.Promote(CreateOptions(paths, checkOnly: true));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("FEAT-150 cleanup artifact mismatch", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithScoreIncreaseFromObservedEvidence_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegister(paths, register =>
        {
            register["scoreChanges"]!.AsArray().Add(new JsonObject
            {
                ["scoreChangeId"] = "RDY-SCORE-20260519-002",
                ["dimensionId"] = "RDY-DIM-003",
                ["direction"] = "increase",
                ["previousScore"] = 3,
                ["proposedScore"] = 4,
                ["acceptedScore"] = 4,
                ["evidenceIds"] = new JsonArray("RDY-EVID-AT-RDY-008-FEAT-136-001"),
                ["sourceGapRow"] = "Cross-device receipt/inclusion verification",
                ["acceptanceGateIds"] = new JsonArray("AT-RDY-008"),
                ["blockerImpactBefore"] = new JsonArray("RDY-BLOCK-FRIENDLY_ORGANIZATION_PILOT-003"),
                ["blockerImpactAfter"] = new JsonArray("RDY-BLOCK-FRIENDLY_ORGANIZATION_PILOT-003"),
                ["claimImpact"] = "No pilot claim change.",
                ["reason"] = "Invalid test score increase.",
                ["generatedDiff"] = "RDY-DIM-003 3 -> 4",
                ["signoffs"] = CreateTwoHatSignoffs(),
            });
        });

        var act = () => new ReadinessRegisterPromotionService().Promote(CreateOptions(paths));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("non-accepted evidence", StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public void Promote_WithAcceptedEvidenceMissingSignoff_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegister(paths, register =>
        {
            var evidence = register["evidenceItems"]!.AsArray()
                .Select(x => x!.AsObject())
                .Single(x => x["evidenceId"]!.GetValue<string>() == "RDY-EVID-AT-RDY-001-FEAT-130-001");
            evidence["signoffs"]!.AsObject().Remove("operationsProduct");
        });

        var act = () => new ReadinessRegisterPromotionService().Promote(CreateOptions(paths));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("operationsProduct", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithCatalogHashConflict_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        var service = new ReadinessRegisterPromotionService();
        service.Promote(CreateOptions(paths));
        MutateRegister(paths, register => register["sourceCommit"] = "changed-after-promotion");

        var act = () => service.Promote(CreateOptions(paths));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Message.Contains("catalog", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Promote_WithUnsafeOutputPath_FailsBeforeWriting()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        var unsafePaths = paths with { OutputRoot = Path.GetTempPath() };

        var act = () => new ReadinessRegisterPromotionService().Promote(CreateOptions(unsafePaths));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Message.Contains("Output root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Promote_PublicSafeSummary_HidesScoresHashesAndPrivateRefs()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);

        var result = new ReadinessRegisterPromotionService().Promote(CreateOptions(paths));
        var publicSummary = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.PublicSafeSummaryFileName));

        publicSummary.Should().Contain("## Current Public-Safe Status");
        publicSummary.Should().Contain("## Non-Claims");
        publicSummary.Should().NotContain($"{CurrentTotalScore}/100");
        var normalizedSummary = publicSummary.ToLowerInvariant();
        normalizedSummary.Should().NotContain("total score");
        normalizedSummary.Should().NotContain("sha-256");
        normalizedSummary.Should().NotContain("restricted_reviewer");
    }

    [Fact]
    public void Promote_BaselineClaims_ArePreserved()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);

        var result = new ReadinessRegisterPromotionService().Promote(CreateOptions(paths));
        var scorecard = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.ScorecardFileName));

        result.TotalScore.Should().Be(CurrentTotalScore);
        result.StrongestAllowedClaim.Should().Be("internal_non_binding_rehearsal");
        scorecard.Should().Contain("internal_non_binding_rehearsal");
        scorecard.Should().Contain("allowed_with_limitations");
        scorecard.Should().Contain("Current strongest allowed claim: internal_non_binding_rehearsal");
        scorecard.Should().Contain("Strongest claim allowed by v1 policy ceiling: friendly_organization_pilot");
        scorecard.Should().Contain("friendly_organization_pilot");
        scorecard.Should().Contain("blocked");
        scorecard.Should().Contain("RDY-SCORE-20260519-001");
        scorecard.Should().Contain("RDY-SCORE-20260525-001");
        scorecard.Should().Contain("RDY-EVID-AT-RDY-012-FEAT-140-001");
        scorecard.Should().Contain("RDY-BLOCK-FRIENDLY_ORGANIZATION_PILOT-001");
        scorecard.Should().Contain("green | resolved | FEAT-131");
    }

    [Fact]
    public void Promote_WithFriendlyPilotAllowed_DerivesScorecardGoNoGoFromClaimState()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForFriendlyPilotClaim(paths);

        var result = new ReadinessRegisterPromotionService().Promote(CreateOptions(paths));
        var scorecard = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.ScorecardFileName));

        result.StrongestAllowedClaim.Should().Be("friendly_organization_pilot");
        scorecard.Should().Contain("Current strongest allowed claim: friendly_organization_pilot");
        scorecard.Should().Contain("Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; production rollout and public/state election readiness remain blocked.");
        scorecard.Should().NotContain("Current go/no-go result: internal non-binding rehearsal is allowed with limitations; pilot and stronger claims are blocked.");
    }

    [Fact]
    public void Promote_WithRedClaimAllowedWithLimitations_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegister(paths, register =>
        {
            var pilotClaim = register["claimLevels"]!.AsArray()
                .Select(x => x!.AsObject())
                .Single(x => x["claimLevel"]!.GetValue<string>() == "friendly_organization_pilot");
            pilotClaim["status"] = "allowed_with_limitations";
            pilotClaim["limitationWording"] = "Invalid test limitation.";
        });

        var act = () => new ReadinessRegisterPromotionService().Promote(CreateOptions(paths));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("red and cannot be allowed", StringComparison.OrdinalIgnoreCase)));
    }

    private static string CreateWorkspace()
    {
        return HushVotingReadinessTestArtifacts.CreateEmptyWorkspace("hush-feat-130-");
    }

    private static void CopyBaselineSources(ReadinessRegisterPromotionPaths paths)
    {
        HushVotingReadinessTestArtifacts.CopyReadinessRegisterSources(paths);
    }

    private static ReadinessRegisterPromotionOptions CreateOptions(
        ReadinessRegisterPromotionPaths paths,
        bool validateOnly = false,
        bool checkOnly = false,
        string? publicationStatus = "not_for_publication") =>
        new(
            paths,
            "hushvoting-readiness-register",
            CurrentRegisterVersion,
            publicationStatus,
            validateOnly,
            Scaffold: false,
            FixedGeneratedAt,
            checkOnly);

    private static void WriteFeat147PromotionSource(
        ReadinessRegisterPromotionPaths paths,
        bool forceBadArtifactHash = false)
    {
        var register = JsonNode.Parse(File.ReadAllText(paths.RegisterPath))!.AsObject();
        var evidencePath = Path.Combine(
            paths.WorkspaceRoot,
            "hush-documents",
            "PrivateServer_ElectronicVoting",
            "Friendly-Pilot-Readiness-Promotion",
            "unit-test-evidence.txt");
        Directory.CreateDirectory(Path.GetDirectoryName(evidencePath)!);
        File.WriteAllText(evidencePath, "FEAT-147 unit test evidence\n");
        var evidenceHash = forceBadArtifactHash
            ? new string('0', 64)
            : ComputeSha256Hex(File.ReadAllBytes(evidencePath));
        var evidenceRelativePath = Path.GetRelativePath(paths.WorkspaceRoot, evidencePath).Replace('\\', '/');

        var decisions = new JsonArray();
        foreach (var blocker in register["blockers"]!.AsArray().Select(node => node!.AsObject()))
        {
            var blockerId = blocker["blockerId"]!.GetValue<string>();
            var severity = blocker["severity"]!.GetValue<string>();
            var status = blocker["status"]!.GetValue<string>();
            var evidenceRefs = new JsonArray();
            if (blockerId == "RDY-BLOCK-FRIENDLY_ORGANIZATION_PILOT-001")
            {
                evidenceRefs.Add(new JsonObject
                {
                    ["artifactId"] = "FEAT147-UNIT-TEST-EVIDENCE-001",
                    ["evidenceId"] = "RDY-EVID-FEAT147-UNIT-TEST-001",
                    ["featureSlice"] = "FEAT-147",
                    ["relativePath"] = evidenceRelativePath,
                    ["sha256Hash"] = evidenceHash,
                    ["hashAlgorithm"] = "sha256",
                    ["mediaType"] = "text/plain",
                    ["visibility"] = "restricted_reviewer",
                    ["freshness"] = "current",
                });
            }

            decisions.Add(new JsonObject
            {
                ["blockerId"] = blockerId,
                ["currentSeverity"] = severity,
                ["currentStatus"] = status,
                ["proposedSeverity"] = severity,
                ["proposedStatus"] = status,
                ["featureSlice"] = "FEAT-147",
                ["acceptanceGateIds"] = new JsonArray("AT-RDY-TEST"),
                ["dimensionIds"] = new JsonArray("RDY-DIM-010"),
                ["decision"] = GetFeat147Decision(blockerId, severity, status),
                ["decisionReason"] = "Unit-test FEAT-147 decision source matches the promoted register blocker state.",
                ["evidenceRefs"] = evidenceRefs,
                ["scoreImpact"] = new JsonObject
                {
                    ["previousScore"] = register["score"]!["total"]!.GetValue<int>(),
                    ["acceptedScore"] = register["score"]!["total"]!.GetValue<int>(),
                    ["dimensionId"] = "RDY-DIM-010",
                },
                ["claimImpact"] = "Unit-test claim impact keeps the source and promoted register aligned.",
                ["residualRisk"] = "Unit-test residual risk remains explicit.",
                ["signoffs"] = CreateTwoHatSignoffs(),
            });
        }

        var source = new JsonObject
        {
            ["schemaVersion"] = "feat147-promotion-source.v1",
            ["sourceId"] = "FEAT147-UNIT-TEST-SOURCE",
            ["generatedAt"] = FixedGeneratedAt.ToString("O"),
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersion"] = CurrentRegisterVersion,
                ["registerVersionId"] = CurrentRegisterVersionId,
            },
            ["targetRegister"] = new JsonObject
            {
                ["registerVersion"] = register["registerVersion"]!.GetValue<string>(),
                ["registerVersionId"] = register["registerVersionId"]!.GetValue<string>(),
                ["targetTotalScore"] = register["score"]!["total"]!.GetValue<int>(),
                ["strongestAllowedClaim"] = "internal_non_binding_rehearsal",
                ["publicationStatus"] = register["generatedViews"]!["publicSafePublicationStatus"]!.GetValue<string>(),
            },
            ["blockerDecisions"] = decisions,
        };

        var sourcePath = Path.Combine(
            paths.WorkspaceRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            "Friendly-Pilot-Readiness-Promotion",
            "examples",
            "release-baseline",
            "feat147-promotion-source.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, source.ToJsonString(JsonOptions));
    }

    private static void MutateRegisterForFriendlyPilotClaim(ReadinessRegisterPromotionPaths paths)
    {
        MutateRegister(paths, register =>
        {
            var friendlyClaim = FindClaim(register, "friendly_organization_pilot");
            friendlyClaim["blockerSeverity"] = "amber";
            friendlyClaim["status"] = "allowed_with_limitations";
            friendlyClaim["allowedWording"] = "HushVoting may be used for controlled friendly-organization pilot planning when limitations remain explicit and private readiness review is available.";
            friendlyClaim["limitationWording"] = "Friendly-pilot readiness is limited to controlled organizations and does not claim production rollout, public/state election readiness, independent validation, or legal sufficiency.";
            friendlyClaim["blockerIds"] = new JsonArray();
        });
    }

    private static void MutateRegisterForFeat150Cleanup(ReadinessRegisterPromotionPaths paths)
    {
        MutateRegister(paths, register =>
        {
            var internalClaim = FindClaim(register, "internal_non_binding_rehearsal");
            internalClaim["blockerSeverity"] = "amber";
            internalClaim["status"] = "allowed_with_limitations";
            internalClaim["blockerIds"] = new JsonArray();

            var friendlyClaim = FindClaim(register, "friendly_organization_pilot");
            friendlyClaim["blockerSeverity"] = "amber";
            friendlyClaim["status"] = "allowed_with_limitations";
            friendlyClaim["allowedWording"] = "HushVoting may be used for controlled friendly-organization pilot planning when limitations remain explicit and private readiness review is available.";
            friendlyClaim["limitationWording"] = "Friendly-pilot readiness is limited to controlled organizations and does not claim production rollout, public/state election readiness, independent validation, or legal sufficiency.";
            friendlyClaim["blockerIds"] = new JsonArray();

            var blocker = FindBlocker(register, "RDY-BLOCK-INTERNAL_NON_BINDING_REHEARSAL-001");
            blocker["severity"] = "green";
            blocker["status"] = "resolved";
            blocker["featureId"] = "FEAT-150";
            blocker["resolutionCriteria"] = "Resolved by FEAT-150 generated-view consistency evidence while the non-binding limitation remains visible.";
        });
    }

    private static void WriteFeat150CleanupSource(ReadinessRegisterPromotionPaths paths)
    {
        var register = JsonNode.Parse(File.ReadAllText(paths.RegisterPath))!.AsObject();
        var source = new JsonObject
        {
            ["schemaVersion"] = "feat150-cleanup-source.v1",
            ["sourceId"] = "FEAT150-UNIT-TEST-CLEANUP-SOURCE",
            ["generatedAt"] = FixedGeneratedAt.ToString("O"),
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersion"] = CurrentRegisterVersion,
                ["registerVersionId"] = CurrentRegisterVersionId,
                ["targetTotalScore"] = register["score"]!["total"]!.GetValue<int>(),
                ["strongestAllowedClaim"] = "friendly_organization_pilot",
            },
            ["targetRegister"] = new JsonObject
            {
                ["registerVersion"] = register["registerVersion"]!.GetValue<string>(),
                ["registerVersionId"] = register["registerVersionId"]!.GetValue<string>(),
                ["targetTotalScore"] = register["score"]!["total"]!.GetValue<int>(),
                ["strongestAllowedClaim"] = "friendly_organization_pilot",
                ["publicationStatus"] = register["generatedViews"]!["publicSafePublicationStatus"]!.GetValue<string>(),
            },
            ["blockerDecision"] = new JsonObject
            {
                ["blockerId"] = "RDY-BLOCK-INTERNAL_NON_BINDING_REHEARSAL-001",
                ["currentSeverity"] = "amber",
                ["currentStatus"] = "open",
                ["proposedSeverity"] = "green",
                ["proposedStatus"] = "resolved",
                ["featureSlice"] = "FEAT-150",
                ["acceptanceGateIds"] = new JsonArray("AT-RDY-001"),
                ["dimensionIds"] = new JsonArray("RDY-DIM-001"),
                ["decision"] = "resolve",
                ["decisionReason"] = "Unit-test FEAT-150 cleanup proves generated views preserve the non-binding limitation while removing the stale blocker.",
                ["evidenceRefs"] = new JsonArray(
                    new JsonObject
                    {
                        ["artifactId"] = "FEAT150-UNIT-SCORECARD",
                        ["path"] = "readiness-scorecard.md",
                        ["visibility"] = "restricted_reviewer",
                    }),
                ["scoreImpact"] = new JsonObject
                {
                    ["type"] = "none",
                    ["previousScore"] = register["score"]!["total"]!.GetValue<int>(),
                    ["acceptedScore"] = register["score"]!["total"]!.GetValue<int>(),
                    ["reason"] = "Blocker cleanup only.",
                },
                ["claimImpact"] = "No claim expansion; internal rehearsal and friendly pilot remain limited.",
                ["residualRisk"] = "Internal rehearsals must remain labelled non-binding.",
                ["signoffs"] = CreateTwoHatSignoffs(),
            },
            ["generatedViewExpectations"] = new JsonObject
            {
                ["scorecardRequiredPhrases"] = new JsonArray(
                    "Current strongest allowed claim: friendly_organization_pilot",
                    "Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; production rollout and public/state election readiness remain blocked.",
                    "internal_non_binding_rehearsal",
                    "allowed_with_limitations"),
                ["scorecardForbiddenPhrases"] = new JsonArray(
                    "Current go/no-go result: internal non-binding rehearsal is allowed with limitations; pilot and stronger claims are blocked."),
                ["restrictedRequiredPhrases"] = new JsonArray(
                    "friendly_organization_pilot",
                    "production_organizational_rollout"),
                ["publicSafeRequiredPhrases"] = new JsonArray(
                    "HushVoting may be discussed for controlled friendly-organization pilot use with explicit limitations.",
                    "Production and public/state election readiness are not claimed in this version."),
                ["publicSafeForbiddenPhrases"] = new JsonArray(
                    "total score",
                    "60/100",
                    "71/100",
                    "sha-256",
                    "restricted_reviewer",
                    "internal/"),
            },
        };

        var sourcePath = Path.Combine(
            paths.WorkspaceRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            "Internal-Non-Binding-Rehearsal-Cleanup",
            "examples",
            "release-baseline",
            "feat150-cleanup-source.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, source.ToJsonString(JsonOptions));
    }

    private static string GetFeat147Decision(string blockerId, string severity, string status)
    {
        if (status == "resolved")
        {
            return "resolve";
        }

        if (severity == "amber")
        {
            return "keep_limited";
        }

        return blockerId.Contains("PRODUCTION", StringComparison.Ordinal) ||
            blockerId.Contains("PUBLIC", StringComparison.Ordinal)
            ? "keep_policy_blocked"
            : "keep_open";
    }

    private static string GetFeat147AuditPackageRoot(ReadinessRegisterPromotionPaths paths) =>
        Path.Combine(
            paths.WorkspaceRoot,
            "hush-documents",
            "PrivateServer_ElectronicVoting",
            "Friendly-Pilot-Readiness-Promotion",
            "package");

    private static string GetFeat150CleanupPackageRoot(ReadinessRegisterPromotionPaths paths) =>
        Path.Combine(
            paths.WorkspaceRoot,
            "hush-documents",
            "PrivateServer_ElectronicVoting",
            "Internal-Non-Binding-Rehearsal-Cleanup",
            "package");

    private static JsonObject FindClaim(JsonObject register, string claimLevel) =>
        register["claimLevels"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(claim => claim["claimLevel"]!.GetValue<string>() == claimLevel);

    private static JsonObject FindBlocker(JsonObject register, string blockerId) =>
        register["blockers"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(blocker => blocker["blockerId"]!.GetValue<string>() == blockerId);

    private static string ComputeSha256Hex(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void MutateRegister(ReadinessRegisterPromotionPaths paths, Action<JsonObject> mutate)
    {
        var register = JsonNode.Parse(File.ReadAllText(paths.RegisterPath))!.AsObject();
        mutate(register);
        File.WriteAllText(paths.RegisterPath, register.ToJsonString(JsonOptions));
    }

    private static JsonObject CreateTwoHatSignoffs() =>
        new()
        {
            ["engineering"] = new JsonObject
            {
                ["role"] = "engineering",
                ["signerId"] = "andre-boim",
                ["signerName"] = "Andre Boim",
                ["signedAt"] = "2026-05-19T00:00:00Z",
                ["basis"] = "Test signoff.",
                ["samePersonTwoHat"] = true,
            },
            ["operationsProduct"] = new JsonObject
            {
                ["role"] = "operations_product",
                ["signerId"] = "andre-boim",
                ["signerName"] = "Andre Boim",
                ["signedAt"] = "2026-05-19T00:00:00Z",
                ["basis"] = "Test signoff.",
                ["samePersonTwoHat"] = true,
            },
        };
}
