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
        scorecard.Should().Contain("Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; production rollout is future-gated by the 95+ hardening plan and public/state election readiness remains an external boundary.");
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
        scorecard.Should().Contain("Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; production rollout is future-gated by the 95+ hardening plan and public/state election readiness remains an external boundary.");
        scorecard.Should().NotContain("Current go/no-go result: internal non-binding rehearsal is allowed with limitations; pilot and stronger claims are blocked.");
    }

    [Fact]
    public void Promote_WithProductionRolloutLimitedRegister_DerivesProductionClaim()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForProductionRolloutLimitedClaim(paths);

        var result = new ReadinessRegisterPromotionService().Promote(CreateOptions(
            paths,
            publicationStatus: null,
            version: null));
        var scorecard = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.ScorecardFileName));
        var publicSummary = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.PublicSafeSummaryFileName));

        result.RegisterVersion.Should().Be("v0.1.6");
        result.RegisterVersionId.Should().Be("RDY-REG-v0.1.6");
        result.TotalScore.Should().Be(80);
        result.PublicationStatus.Should().Be("production_rollout_with_limitations");
        result.StrongestAllowedClaim.Should().Be("production_organizational_rollout");
        scorecard.Should().Contain("Current strongest allowed claim: production_organizational_rollout");
        scorecard.Should().Contain("limited organizational rollout is allowed with limitations; public/state election readiness remains an external boundary.");
        publicSummary.Should().Contain("limited organizational rollout with explicit limitations");
        publicSummary.ToLowerInvariant().Should().NotContain("total score");
    }

    [Fact]
    public void Promote_WithFeat156Source_AppliesProductionRolloutRegisterPromotion()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForFeat156Baseline(paths);
        WriteCompletedFeatureFoldersForFeat156(paths);
        WriteFeat156PromotionSource(paths);
        var service = new ReadinessRegisterPromotionService();

        var options = new ReadinessRegisterPromotionOptions(
            paths,
            "hushvoting-readiness-register",
            "v0.1.6",
            "production_rollout_with_limitations",
            ValidateOnly: false,
            Scaffold: false,
            GeneratedAt: null);
        var result = service.Promote(options);
        var promotedRegister = JsonNode.Parse(File.ReadAllText(Path.Combine(
            result.VersionOutputRoot,
            ReadinessRegisterPromotionService.RegisterFileName)))!.AsObject();
        var scorecard = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.ScorecardFileName));
        var reviewerRoot = GetFeat156ReviewerPackageRoot(paths);
        var publicSafeSummary = File.ReadAllText(Path.Combine(reviewerRoot, "feat156-public-safe-summary.md"));
        var restrictedIndex = JsonNode.Parse(File.ReadAllText(Path.Combine(reviewerRoot, "feat156-restricted-reviewer-index.json")))!.AsObject();
        var forbiddenScan = JsonNode.Parse(File.ReadAllText(Path.Combine(reviewerRoot, "feat156-forbidden-material-scan.json")))!.AsObject();
        var noUiNote = File.ReadAllText(Path.Combine(reviewerRoot, "feat156-no-ui-boundary-note.md"));

        result.RegisterVersionId.Should().Be("RDY-REG-v0.1.6");
        result.GeneratedAt.Should().Be(new DateTimeOffset(2026, 5, 31, 12, 0, 0, TimeSpan.Zero));
        result.TotalScore.Should().Be(80);
        result.StrongestAllowedClaim.Should().Be("production_organizational_rollout");
        FindDimension(promotedRegister, "RDY-DIM-002")["currentScore"]!.GetValue<int>().Should().Be(8);
        FindDimension(promotedRegister, "RDY-DIM-010")["currentScore"]!.GetValue<int>().Should().Be(8);
        promotedRegister["evidenceItems"]!.AsArray()
            .Select(node => node!.AsObject()["evidenceId"]!.GetValue<string>())
            .Should()
            .Contain("RDY-EVID-AT-RDY-013-FEAT-156-001");
        promotedRegister["scoreChanges"]!.AsArray()
            .Select(node => node!.AsObject()["scoreChangeId"]!.GetValue<string>())
            .Should()
            .Contain("RDY-SCORE-20260531-006");
        scorecard.Should().Contain("RDY-SCORE-20260531-006");
        scorecard.Should().Contain("production_organizational_rollout");
        File.Exists(Path.Combine(reviewerRoot, "feat156-production-rollout-decision-ledger.json")).Should().BeTrue();
        File.Exists(Path.Combine(reviewerRoot, "feat156-promotion-source-snapshot.json")).Should().BeTrue();
        File.Exists(Path.Combine(reviewerRoot, "feat156-artifact-hash-audit.json")).Should().BeTrue();
        File.Exists(Path.Combine(reviewerRoot, "feat156-package-manifest.json")).Should().BeTrue();
        publicSafeSummary.Should().Contain(result.ManifestHash);
        publicSafeSummary.Should().Contain(result.ArchiveHash);
        publicSafeSummary.Should().Contain("limited organizational rollout readiness");
        publicSafeSummary.ToLowerInvariant().Should().NotContain("total score");
        publicSafeSummary.Should().NotContain("restricted_reviewer");
        publicSafeSummary.Should().NotContain("legal sufficiency");
        publicSafeSummary.Should().NotContain("independent certification");
        publicSafeSummary.Should().NotContain("full AGM");
        restrictedIndex["rawEvidenceInlined"]!.GetValue<bool>().Should().BeFalse();
        restrictedIndex["payloadInliningAllowed"]!.GetValue<bool>().Should().BeFalse();
        restrictedIndex["evidenceIndex"]!.AsArray().Should().HaveCount(6);
        restrictedIndex["evidenceIndex"]!.AsArray()
            .Select(node => node!.AsObject()["featureId"]!.GetValue<string>())
            .Should()
            .Contain("FEAT-156");
        forbiddenScan["status"]!.GetValue<string>().Should().Be("passed");
        noUiNote.Should().Contain("No HushWebClient route or component is required");

        service.Promote(options with { CheckOnly = true }).RegisterVersionId.Should().Be("RDY-REG-v0.1.6");
    }

    [Fact]
    public void Promote_WithInternalAudit95Source_AppliesV018Promotion()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForFeat156Baseline(paths);
        WriteCompletedFeatureFoldersForFeat156(paths);
        WriteFeat156PromotionSource(paths, targetVersion: "v0.1.7");
        var service = new ReadinessRegisterPromotionService();
        var baseline = service.Promote(new ReadinessRegisterPromotionOptions(
            paths,
            "hushvoting-readiness-register",
            "v0.1.7",
            "pilot_only_with_limitations",
            ValidateOnly: false,
            Scaffold: false,
            GeneratedAt: null));
        var finalPaths = paths with { SourceRoot = baseline.VersionOutputRoot };
        WriteInternalAudit95PromotionSource(finalPaths);

        var options = new ReadinessRegisterPromotionOptions(
            finalPaths,
            "hushvoting-readiness-register",
            "v0.1.8",
            "pilot_only_with_limitations",
            ValidateOnly: false,
            Scaffold: false,
            GeneratedAt: null);
        var result = service.Promote(options);
        var promotedRegister = JsonNode.Parse(File.ReadAllText(Path.Combine(
            result.VersionOutputRoot,
            ReadinessRegisterPromotionService.RegisterFileName)))!.AsObject();
        var scorecard = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.ScorecardFileName));
        var publicSummary = File.ReadAllText(Path.Combine(result.VersionOutputRoot, ReadinessRegisterPromotionService.PublicSafeSummaryFileName));
        var catalog = JsonNode.Parse(File.ReadAllText(finalPaths.CatalogPath))!.AsObject();

        result.RegisterVersionId.Should().Be("RDY-REG-v0.1.8");
        result.GeneratedAt.Should().Be(new DateTimeOffset(2026, 6, 4, 12, 0, 0, TimeSpan.Zero));
        result.TotalScore.Should().Be(95);
        result.StrongestAllowedClaim.Should().Be("friendly_organization_pilot");
        catalog["currentRegisterVersionId"]!.GetValue<string>().Should().Be("RDY-REG-v0.1.8");
        promotedRegister["score"]!["total"]!.GetValue<int>().Should().Be(95);
        promotedRegister["blockers"]!.AsArray()
            .Select(node => node!.AsObject())
            .Where(blocker => blocker["blockerId"]!.GetValue<string>().StartsWith("RDY-BLOCK-INTERNAL_AUDIT_95_DIM", StringComparison.Ordinal))
            .Should()
            .OnlyContain(blocker => blocker["status"]!.GetValue<string>() == "resolved" && blocker["severity"]!.GetValue<string>() == "green");
        promotedRegister["scoreChanges"]!.AsArray()
            .Select(node => node!.AsObject()["scoreChangeId"]!.GetValue<string>())
            .Should()
            .Contain("RDY-SCORE-20260604-010");
        FindDimension(promotedRegister, "RDY-DIM-001")["currentScore"]!.GetValue<int>().Should().Be(10);
        FindDimension(promotedRegister, "RDY-DIM-010")["currentScore"]!.GetValue<int>().Should().Be(9);
        FindClaim(promotedRegister, "production_organizational_rollout")["status"]!.GetValue<string>().Should().Be("future_gated");
        FindClaim(promotedRegister, "public_or_state_election")["status"]!.GetValue<string>().Should().Be("external_boundary");
        var directNonBindingProfile = promotedRegister["claimProfiles"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(profile => profile["profileId"]!.GetValue<string>() == "hushvoting.direct.non_binding");
        var directBindingProfile = promotedRegister["claimProfiles"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(profile => profile["profileId"]!.GetValue<string>() == "hushvoting.direct.binding");
        promotedRegister["claimProfiles"]!.AsArray().Should().HaveCount(10);
        directNonBindingProfile["productMode"]!.GetValue<string>().Should().Be("HushVoting! Direct");
        directNonBindingProfile["bindingStatus"]!.GetValue<string>().Should().Be("Non-Binding");
        directNonBindingProfile["isNonBindingElection"]!.GetValue<bool>().Should().BeTrue();
        directNonBindingProfile["gateSeverity"]!.GetValue<string>().Should().Be("green");
        directNonBindingProfile["gateStatus"]!.GetValue<string>().Should().Be("passed");
        directNonBindingProfile["verifierWarningCount"]!.GetValue<int>().Should().Be(0);
        directNonBindingProfile["verifierWarnings"]!.AsArray()
            .Select(node => node!.AsObject()["resultCode"]!.GetValue<string>())
            .Should()
            .BeEmpty();
        directNonBindingProfile["evidenceRefs"]!.AsArray()
            .Select(node => node!.GetValue<string>())
            .Should()
            .Contain("hush-documents/PrivateServer_ElectronicVoting/Live-Rehearsal-Evidence/HushVoting-Direct-Non-Binding-20260605102141/public-verifier-output-current-public-20260605c/VerifierOutput.json");
        directBindingProfile["productMode"]!.GetValue<string>().Should().Be("HushVoting! Direct");
        directBindingProfile["bindingStatus"]!.GetValue<string>().Should().Be("Binding");
        directBindingProfile["isNonBindingElection"]!.GetValue<bool>().Should().BeFalse();
        directBindingProfile["gateStatus"]!.GetValue<string>().Should().Be("passed");
        directBindingProfile["verifierWarningCount"]!.GetValue<int>().Should().Be(0);
        scorecard.Should().Contain("Total score: 95/100");
        scorecard.Should().Contain("internal-audit-95 hardening is accepted");
        scorecard.Should().Contain("## HushVoting Claim Profiles");
        scorecard.Should().Contain("Non-Binding HushVoting! Direct");
        scorecard.Should().Contain("## Environment Operational Checklists");
        scorecard.Should().Contain("PreProduction is optional");
        scorecard.Should().Contain("Production runs the full readiness workflow directly");
        scorecard.Should().Contain("production-activation-addendum.json");
        scorecard.Should().Contain("claim blocking is decided by the promotion policy, not by editing the checklist");
        scorecard.Should().NotContain("operational_security_access_snapshot_missing");
        scorecard.Should().Contain("Binding HushVoting! Direct");
        scorecard.Should().Contain("passed");
        scorecard.Should().NotContain("production rollout is future-gated by the 95+ hardening plan");
        publicSummary.Should().Contain("Hush-owned internal-audit-95 hardening is accepted");
        publicSummary.ToLowerInvariant().Should().NotContain("total score");

        service.Promote(options with { CheckOnly = true }).RegisterVersionId.Should().Be("RDY-REG-v0.1.8");
    }

    [Fact]
    public void Promote_WithInternalAudit95ProposalTampered_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForFeat156Baseline(paths);
        WriteCompletedFeatureFoldersForFeat156(paths);
        WriteFeat156PromotionSource(paths, targetVersion: "v0.1.7");
        var service = new ReadinessRegisterPromotionService();
        var baseline = service.Promote(new ReadinessRegisterPromotionOptions(
            paths,
            "hushvoting-readiness-register",
            "v0.1.7",
            "pilot_only_with_limitations",
            ValidateOnly: false,
            Scaffold: false,
            GeneratedAt: null));
        var finalPaths = paths with { SourceRoot = baseline.VersionOutputRoot };
        var tamperedArtifact = WriteInternalAudit95PromotionSource(finalPaths);
        File.AppendAllText(tamperedArtifact, "tampered");

        var act = () => service.Promote(new ReadinessRegisterPromotionOptions(
            finalPaths,
            "hushvoting-readiness-register",
            "v0.1.8",
            "pilot_only_with_limitations",
            ValidateOnly: false,
            Scaffold: false,
            GeneratedAt: null));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Message.Contains("Internal audit 95 promotion source failed validation", StringComparison.Ordinal) &&
                x.Details.Any(detail => detail.Contains("hash mismatch", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithFeat156ReviewerArtifactTampered_CheckOnlyFailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForFeat156Baseline(paths);
        WriteCompletedFeatureFoldersForFeat156(paths);
        WriteFeat156PromotionSource(paths);
        var service = new ReadinessRegisterPromotionService();
        var options = new ReadinessRegisterPromotionOptions(
            paths,
            "hushvoting-readiness-register",
            "v0.1.6",
            "production_rollout_with_limitations",
            ValidateOnly: false,
            Scaffold: false,
            GeneratedAt: null);
        service.Promote(options);

        File.AppendAllText(
            Path.Combine(GetFeat156ReviewerPackageRoot(paths), "feat156-public-safe-summary.md"),
            "tampered");

        var act = () => service.Promote(options with { CheckOnly = true });

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("FEAT-156 reviewer output artifact mismatch", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithFeat156ForbiddenPublicSafeNeedle_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForFeat156Baseline(paths);
        WriteCompletedFeatureFoldersForFeat156(paths);
        WriteFeat156PromotionSource(paths, extraForbiddenClaimNeedle: "limited organizational rollout readiness");

        var act = () => new ReadinessRegisterPromotionService().Promote(new ReadinessRegisterPromotionOptions(
            paths,
            "hushvoting-readiness-register",
            "v0.1.6",
            "production_rollout_with_limitations",
            ValidateOnly: false,
            Scaffold: false,
            GeneratedAt: null));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x =>
                x.Message.Contains("FEAT-156 reviewer output validation failed", StringComparison.Ordinal) &&
                x.Details.Any(detail =>
                    detail.Contains("feat156-public-safe-summary.md", StringComparison.Ordinal) &&
                    detail.Contains("limited organizational rollout readiness", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithProductionRolloutScoreBelow80_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForProductionRolloutLimitedClaim(paths, totalScore: 79);

        var act = () => new ReadinessRegisterPromotionService().Promote(CreateOptions(
            paths,
            publicationStatus: null,
            version: null));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("production_rollout_with_limitations requires score.total to be at least 80", StringComparison.Ordinal)));
    }

    [Fact]
    public void Promote_WithPublicStateAllowedInProductionRegister_FailsClosed()
    {
        var tempRoot = CreateWorkspace();
        var paths = ReadinessRegisterPromotionPaths.FromWorkspaceRoot(tempRoot);
        CopyBaselineSources(paths);
        MutateRegisterForProductionRolloutLimitedClaim(paths);
        MutateRegister(paths, register =>
        {
            var publicStateClaim = FindClaim(register, "public_or_state_election");
            publicStateClaim["blockerSeverity"] = "amber";
            publicStateClaim["status"] = "allowed_with_limitations";
            publicStateClaim["limitationWording"] = "Invalid test unlock.";
        });

        var act = () => new ReadinessRegisterPromotionService().Promote(CreateOptions(
            paths,
            publicationStatus: null,
            version: null));

        act.Should().Throw<ReadinessRegisterPromotionException>()
            .Where(x => x.Details.Any(detail => detail.Contains("public_or_state_election must remain red and blocked", StringComparison.Ordinal)));
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
        string? publicationStatus = "not_for_publication",
        string? version = CurrentRegisterVersion) =>
        new(
            paths,
            "hushvoting-readiness-register",
            version,
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

    private static void MutateRegisterForProductionRolloutLimitedClaim(
        ReadinessRegisterPromotionPaths paths,
        int totalScore = 80)
    {
        MutateRegister(paths, register =>
        {
            register["registerVersion"] = "v0.1.6";
            register["registerVersionId"] = "RDY-REG-v0.1.6";
            register["sourceCommit"] = "feat-156-production-limited-unit-test";
            register["score"]!.AsObject()["total"] = totalScore;
            register["generatedViews"]!.AsObject()["publicSafePublicationStatus"] = "production_rollout_with_limitations";
            register["claimPolicy"]!.AsObject()["strongestAllowedV1Claim"] = "production_organizational_rollout";

            foreach (var dimension in register["dimensions"]!.AsArray().Select(node => node!.AsObject()))
            {
                dimension["currentScore"] = 8;
            }

            if (totalScore < 80)
            {
                FindDimension(register, "RDY-DIM-010")["currentScore"] = 7;
            }

            var friendlyClaim = FindClaim(register, "friendly_organization_pilot");
            friendlyClaim["blockerSeverity"] = "amber";
            friendlyClaim["status"] = "allowed_with_limitations";
            friendlyClaim["allowedWording"] = "HushVoting may be used for controlled friendly-organization pilot planning when limitations remain explicit and private readiness review is available.";
            friendlyClaim["limitationWording"] = "Friendly-pilot readiness remains limited below production rollout and does not claim public/state election readiness, independent validation, or legal sufficiency.";
            friendlyClaim["blockerIds"] = new JsonArray();

            var productionClaim = FindClaim(register, "production_organizational_rollout");
            productionClaim["blockerSeverity"] = "amber";
            productionClaim["status"] = "allowed_with_limitations";
            productionClaim["allowedWording"] = "HushVoting may support limited organizational rollout only when residual limits, customer-owned governance responsibilities, and public/state blockers remain visible.";
            productionClaim["limitationWording"] = "Production rollout remains limited and does not claim public/state election readiness, customer authority approval, external certification, or complete meeting operations readiness.";
            productionClaim["blockedWording"] = "";
            productionClaim["publicSafeStatus"] = "production_rollout_with_limitations";
            productionClaim["blockerIds"] = new JsonArray("RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");

            var publicStateClaim = FindClaim(register, "public_or_state_election");
            publicStateClaim["blockerSeverity"] = "red";
            publicStateClaim["status"] = "blocked";
            publicStateClaim["blockedWording"] = "Public or state election readiness remains blocked and requires external authority prerequisites outside this promotion.";
            publicStateClaim["blockerIds"] = new JsonArray("RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");

            var productionBlocker = FindBlocker(register, "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");
            productionBlocker["severity"] = "amber";
            productionBlocker["status"] = "open";
            productionBlocker["featureId"] = "FEAT-156";
            productionBlocker["resolutionCriteria"] = "Limited production rollout remains amber until repeated operating history, customer-site variance evidence, independent validation, and legal/governance prerequisites are accepted.";

            var publicStateBlocker = FindBlocker(register, "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");
            publicStateBlocker["severity"] = "red";
            publicStateBlocker["status"] = "open";
            publicStateBlocker["resolutionCriteria"] = "Public or state election readiness requires external authority, jurisdiction, certification, transparency, accessibility, procurement, and dispute-remedy prerequisites.";
        });
    }

    private static void MutateRegisterForFeat156Baseline(ReadinessRegisterPromotionPaths paths)
    {
        MutateRegister(paths, register =>
        {
            register["registerVersion"] = "v0.1.5";
            register["registerVersionId"] = "RDY-REG-v0.1.5";
            register["sourceCommit"] = "feat-156-unit-test-baseline";
            register["score"]!.AsObject()["total"] = 71;
            register["generatedViews"]!.AsObject()["publicSafePublicationStatus"] = "pilot_only_with_limitations";
            register["claimPolicy"]!.AsObject()["strongestAllowedV1Claim"] = "friendly_organization_pilot";
            register["claimPolicy"]!.AsObject()["alwaysBlockedV1Claims"] = new JsonArray(
                "production_organizational_rollout",
                "public_or_state_election");

            var scores = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["RDY-DIM-001"] = 8,
                ["RDY-DIM-002"] = 6,
                ["RDY-DIM-003"] = 7,
                ["RDY-DIM-004"] = 7,
                ["RDY-DIM-005"] = 8,
                ["RDY-DIM-006"] = 8,
                ["RDY-DIM-007"] = 6,
                ["RDY-DIM-008"] = 8,
                ["RDY-DIM-009"] = 6,
                ["RDY-DIM-010"] = 7,
            };
            foreach (var dimension in register["dimensions"]!.AsArray().Select(node => node!.AsObject()))
            {
                dimension["currentScore"] = scores[dimension["dimensionId"]!.GetValue<string>()];
            }

            var friendlyClaim = FindClaim(register, "friendly_organization_pilot");
            friendlyClaim["blockerSeverity"] = "amber";
            friendlyClaim["status"] = "allowed_with_limitations";
            friendlyClaim["allowedWording"] = "HushVoting may be used for controlled friendly-organization pilot planning when limitations remain explicit and private readiness review is available.";
            friendlyClaim["limitationWording"] = "Friendly-pilot readiness is limited to controlled organizations and does not claim production rollout, public/state election readiness, independent validation, or legal sufficiency.";
            friendlyClaim["blockerIds"] = new JsonArray();

            var productionClaim = FindClaim(register, "production_organizational_rollout");
            productionClaim["blockerSeverity"] = "red";
            productionClaim["status"] = "blocked";
            productionClaim["allowedWording"] = "";
            productionClaim["limitationWording"] = "";
            productionClaim["blockedWording"] = "Production organizational rollout is blocked by the v1 claim policy.";
            productionClaim["publicSafeStatus"] = "public_claim_blocked";
            productionClaim["blockerIds"] = new JsonArray("RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");

            var publicStateClaim = FindClaim(register, "public_or_state_election");
            publicStateClaim["blockerSeverity"] = "red";
            publicStateClaim["status"] = "blocked";
            publicStateClaim["allowedWording"] = "";
            publicStateClaim["limitationWording"] = "";
            publicStateClaim["blockedWording"] = "Public or state election readiness is blocked by the v1 claim policy.";
            publicStateClaim["publicSafeStatus"] = "public_claim_blocked";
            publicStateClaim["blockerIds"] = new JsonArray("RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");

            var productionBlocker = FindBlocker(register, "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001");
            productionBlocker["severity"] = "red";
            productionBlocker["status"] = "open";
            productionBlocker["featureId"] = "FEAT-130";
            productionBlocker["limitationWording"] = "";
            productionBlocker["resolutionCriteria"] = "A later readiness register version explicitly supersedes the v1 claim policy with accepted evidence.";

            var publicStateBlocker = FindBlocker(register, "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001");
            publicStateBlocker["severity"] = "red";
            publicStateBlocker["status"] = "open";
            publicStateBlocker["featureId"] = "FEAT-130";
            publicStateBlocker["limitationWording"] = "";
            publicStateBlocker["resolutionCriteria"] = "A later readiness register version explicitly supersedes the v1 claim policy with accepted evidence and external prerequisites.";
        });
    }

    private static void WriteCompletedFeatureFoldersForFeat156(ReadinessRegisterPromotionPaths paths)
    {
        var completedRoot = Path.Combine(paths.WorkspaceRoot, "hush-memory-bank", "Features", "04_COMPLETED");
        foreach (var featureId in new[] { "FEAT-151", "FEAT-152", "FEAT-153", "FEAT-154", "FEAT-155" })
        {
            Directory.CreateDirectory(Path.Combine(completedRoot, $"{featureId}-unit-test-completed"));
        }
    }

    private static void WriteFeat156PromotionSource(
        ReadinessRegisterPromotionPaths paths,
        string? extraForbiddenClaimNeedle = null,
        string targetVersion = "v0.1.6")
    {
        var internalAuditPlanningTarget = targetVersion == "v0.1.7";
        var forbiddenClaimNeedles = new JsonArray(
            "production green",
            "public/state election ready",
            "legal sufficiency",
            "independent certification",
            "full AGM management software",
            "government election ready",
            "legally binding AGM platform");
        if (!string.IsNullOrWhiteSpace(extraForbiddenClaimNeedle))
        {
            forbiddenClaimNeedles.Add(extraForbiddenClaimNeedle);
        }

        var source = new JsonObject
        {
            ["schemaVersion"] = "production-rollout-promotion-source.v1",
            ["sourceId"] = "FEAT156-UNIT-TEST-PROMOTION-SOURCE",
            ["featureId"] = "FEAT-156",
            ["status"] = "accepted",
            ["generatedAt"] = "2026-05-31T12:00:00Z",
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = "RDY-REG-v0.1.5",
                ["registerVersion"] = "v0.1.5",
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 71,
                ["strongestAllowedClaim"] = "friendly_organization_pilot",
                ["publicationStatus"] = "pilot_only_with_limitations",
            },
            ["targetRegister"] = new JsonObject
            {
                ["registerVersionId"] = $"RDY-REG-{targetVersion}",
                ["registerVersion"] = targetVersion,
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 80,
                ["strongestAllowedClaim"] = internalAuditPlanningTarget
                    ? "friendly_organization_pilot"
                    : "production_organizational_rollout",
                ["publicationStatus"] = internalAuditPlanningTarget
                    ? "pilot_only_with_limitations"
                    : "production_rollout_with_limitations",
                ["productionClaim"] = new JsonObject
                {
                    ["claimLevel"] = "production_organizational_rollout",
                    ["severity"] = "amber",
                    ["status"] = internalAuditPlanningTarget ? "future_gated" : "allowed_with_limitations",
                    ["publicSafeStatus"] = internalAuditPlanningTarget
                        ? "not_ready_for_public_claim"
                        : "production_rollout_with_limitations",
                    ["wording"] = internalAuditPlanningTarget
                        ? "Production organizational rollout is a future execution gate after Hush-owned 95+ internal audit hardening is complete; then rehearsal and binding-election proof validity can be produced and verified."
                        : "HushVoting may support limited organizational rollout only when promoted evidence, residual limits, customer-owned governance responsibilities, and public or state blockers remain visible.",
                },
                ["publicStateClaim"] = new JsonObject
                {
                    ["claimLevel"] = "public_or_state_election",
                    ["severity"] = internalAuditPlanningTarget ? "amber" : "red",
                    ["status"] = internalAuditPlanningTarget ? "external_boundary" : "blocked",
                    ["publicSafeStatus"] = "public_claim_blocked",
                    ["wording"] = internalAuditPlanningTarget
                        ? "Public or state election readiness is a downstream external boundary; Hush can prepare technical evidence, but authority, jurisdiction, certification, transparency, accessibility, procurement, and dispute-remedy prerequisites sit outside this internal audit report."
                        : "Public or state election readiness remains blocked and requires external authority, jurisdiction, certification, transparency, accessibility, procurement, and dispute-remedy prerequisites outside this promotion.",
                },
            },
            ["scoreModel"] = new JsonObject
            {
                ["baselineTotal"] = 71,
                ["acceptedInputDelta"] = 8,
                ["feat156Delta"] = 1,
                ["targetTotal"] = 80,
                ["minimumProductionLimitedScore"] = internalAuditPlanningTarget ? null : 80,
                ["internalAuditTargetScore"] = internalAuditPlanningTarget ? 95 : null,
                ["scoreCannotBypassBlockers"] = true,
            },
            ["scoreMovements"] = new JsonArray(
                CreateFeat156Movement("FEAT-151", "RDY-DIM-002", 6, 8, 2, "accepted", "AT-RDY-007", "Verifier/sample/tamper corpus", "RDY-EVID-AT-RDY-007-FEAT-151-001", "FEAT151-CORPUS-MANIFEST", "bd6d7d179368fbb7a13811d2fea497ad68306efd949a8178778ca2890554a48c"),
                CreateFeat156Movement("FEAT-152", "RDY-DIM-003", 7, 8, 1, "accepted", "AT-RDY-008", "Cross-device receipt/inclusion verification", "RDY-EVID-AT-RDY-008-FEAT-152-001", "FEAT152-RECEIPT-CHANNEL-MANIFEST", "d9b09012846bab1d07b7082c88fdd70c206160b0b31dd38a9655e440d5ec2c64"),
                CreateFeat156Movement("FEAT-153", "RDY-DIM-004", 7, 8, 1, "accepted", "AT-RDY-001", "Protocol/evidence architecture", "RDY-EVID-AT-RDY-001-FEAT-153-001", "FEAT153-PUBLICATION-COUNTING-MANIFEST", "9ae9c5a78d14c4417b8283e6ba996f08e567d5776c540c27bfdfdcebb8742ca3"),
                CreateFeat156Movement("FEAT-154", "RDY-DIM-007", 6, 8, 2, "accepted", "AT-RDY-006", "Operational readiness package", "RDY-EVID-AT-RDY-006-FEAT-154-001", "FEAT154-PRODUCTION-LIKE-RUN-MANIFEST", "62b2c9afb605bb6e0d26876629b7df122b7da566df37f536b4790a9398ecb410"),
                CreateFeat156Movement("FEAT-155", "RDY-DIM-009", 6, 8, 2, "accepted", "AT-RDY-011", "Dispute/continuity readiness", "RDY-EVID-AT-RDY-011-FEAT-155-001", "FEAT155-FAILED-FINALIZE-MANIFEST", "9ca42435559bbcc5b91ce99428a100e14d1637f60e0947eff21d869f8b36037b"),
                CreateFeat156Movement("FEAT-156", "RDY-DIM-010", 7, 8, 1, "accepted_with_limitations", "AT-RDY-013", "Controlled pilot evidence", "RDY-EVID-AT-RDY-013-FEAT-156-001", "FEAT156-PROMOTION-CONTRACT", "867cb50db400715fb444fd6e2d7e15763e6d84bc054b36c00bda3ddbaadf51ec")),
            ["policyBaselines"] = new JsonArray(
                new JsonObject { ["featureId"] = "FEAT-148" },
                new JsonObject { ["featureId"] = "FEAT-149" }),
            ["evidenceLifecyclePolicy"] = new JsonObject
            {
                ["requiredCompletedFeatures"] = new JsonArray("FEAT-151", "FEAT-152", "FEAT-153", "FEAT-154", "FEAT-155"),
                ["freshnessRequired"] = "current",
                ["tamperCheckRequired"] = true,
                ["placeholderInputsBlock"] = true,
            },
            ["blockerDecisions"] = new JsonArray(
                new JsonObject
                {
                    ["blockerId"] = "RDY-BLOCK-PRODUCTION_ORGANIZATIONAL_ROLLOUT-001",
                    ["claimLevel"] = "production_organizational_rollout",
                    ["targetSeverity"] = internalAuditPlanningTarget ? "red" : "amber",
                    ["targetStatus"] = internalAuditPlanningTarget ? "superseded" : "allowed_with_limitations",
                    ["decision"] = internalAuditPlanningTarget ? "replace_with_internal_audit_95_plan" : "allow_with_limitations",
                    ["limitationWording"] = internalAuditPlanningTarget
                        ? "The old production-policy blocker is superseded by the Hush-owned internal audit 95+ hardening plan."
                        : "Allowed only for limited organizational rollout with visible residual risks and customer-owned governance responsibilities.",
                    ["residualRisk"] = internalAuditPlanningTarget
                        ? "The current promoted score is 80/100 against a Hush-owned 95+ internal audit target; the gap is owned by Hush and tracked as internal hardening blockers."
                        : "Repeated operating history, customer-site variance, independent validation, and legal sufficiency remain unproven.",
                },
                new JsonObject
                {
                    ["blockerId"] = "RDY-BLOCK-PUBLIC_OR_STATE_ELECTION-001",
                    ["claimLevel"] = "public_or_state_election",
                    ["targetSeverity"] = internalAuditPlanningTarget ? "red" : "red",
                    ["targetStatus"] = internalAuditPlanningTarget ? "superseded" : "open",
                    ["decision"] = internalAuditPlanningTarget ? "move_to_downstream_report" : "keep_policy_blocked",
                    ["limitationWording"] = "No public or state election readiness is claimed by this register.",
                    ["residualRisk"] = "Jurisdiction, authority approval, certification, public accessibility, procurement, transparency, and dispute-remedy prerequisites remain outside this promotion.",
                }),
            ["claimPolicy"] = new JsonObject
            {
                ["productionGreenForbidden"] = true,
                ["publicStateUnlockForbidden"] = true,
                ["legalSufficiencyForbidden"] = true,
                ["independentCertificationForbidden"] = true,
                ["fullAgmProductClaimForbidden"] = true,
            },
            ["publicSafeOutputRules"] = new JsonObject
            {
                ["allowedPublicFields"] = new JsonArray(
                    "registerId",
                    "registerVersionId",
                    "publicationStatus",
                    "publicSafeStatus",
                    "manifestHash",
                    "archiveHash",
                    "limitationWording"),
                ["allowedPublicPhrases"] = new JsonArray(
                    "limited organizational rollout readiness",
                    "not legal approval",
                    "customer-owned governance responsibilities remain visible"),
                ["forbiddenMaterialNeedles"] = new JsonArray(
                    "C:\\myWork\\HushNetworkOrg",
                    "hush-documents/PrivateServer_ElectronicVoting/",
                    "restricted-evidence/",
                    "restricted_reviewer",
                    "raw evidence",
                    "raw log",
                    "voter identity",
                    "ballot choice",
                    "KMS key",
                    "credential",
                    "support case",
                    "database connection"),
                ["forbiddenClaimNeedles"] = forbiddenClaimNeedles,
                ["numericScorePublicDisclosure"] = false,
            },
            ["restrictedReviewerRules"] = new JsonObject
            {
                ["payloadInliningAllowed"] = false,
                ["allowedRefTypes"] = new JsonArray(
                    "path",
                    "sha256",
                    "manifest_entry",
                    "source_id",
                    "feature_id",
                    "acceptance_gate"),
                ["rawEvidenceCopied"] = false,
            },
            ["signoff"] = new JsonObject
            {
                ["engineeringRole"] = "engineering-owner",
                ["operationsProductRole"] = "operations-product-owner",
                ["status"] = "accepted",
                ["samePersonTwoHatAllowed"] = true,
            },
            ["residualRisks"] = new JsonArray(
                "RDY-REG-v0.1.6 can support limited organizational rollout only with visible limitations.",
                "External authority election use remains outside this promotion.",
                "Customer governance and regulatory approval remain customer-owned.",
                "One production-like run plus one continuity rehearsal does not prove repeated production operating history.",
                "Public-safe outputs must not expose internal score details, source payloads, private paths, or overclaim wording."),
        };

        var sourcePath = Path.Combine(
            paths.WorkspaceRoot,
            "hush-memory-bank",
            "Overview",
            "HushVotingReadiness",
            "Production-Rollout-Promotion-Register",
            "examples",
            "release-baseline",
            "production-rollout-promotion-source.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, source.ToJsonString(JsonOptions));
    }

    private static JsonObject CreateFeat156Movement(
        string featureId,
        string dimensionId,
        int previousScore,
        int acceptedScore,
        int delta,
        string status,
        string acceptanceGateId,
        string sourceGapRow,
        string evidenceId,
        string artifactId,
        string sha256Hash) =>
        new()
        {
            ["movementId"] = $"FEAT156-SCORE-{dimensionId}-{featureId}",
            ["featureId"] = featureId,
            ["dimensionId"] = dimensionId,
            ["previousScore"] = previousScore,
            ["acceptedScore"] = acceptedScore,
            ["delta"] = delta,
            ["status"] = status,
            ["freshness"] = "current",
            ["directRegisterMutation"] = false,
            ["registerPromotionOwner"] = "FEAT-156",
            ["acceptanceGateIds"] = new JsonArray(acceptanceGateId),
            ["sourceGapRows"] = new JsonArray(sourceGapRow),
            ["evidenceIds"] = new JsonArray(evidenceId),
            ["artifactRefs"] = new JsonArray(
                new JsonObject
                {
                    ["artifactId"] = artifactId,
                    ["path"] = $"unit-test/{featureId}/{artifactId}.json",
                    ["sha256Hash"] = sha256Hash,
                    ["hashBasis"] = "file_sha256",
                    ["visibility"] = featureId is "FEAT-151" or "FEAT-152" or "FEAT-153" ? "public" : "restricted",
                }),
            ["claimEffect"] = $"Raises {dimensionId} evidence to {acceptedScore} for FEAT-156 production-limited promotion.",
            ["residualRisk"] = $"{featureId} residual risk remains visible after promotion.",
            ["signoff"] = new JsonObject
            {
                ["sourceFeatureCompleted"] = featureId != "FEAT-156",
                ["acceptedForPromotion"] = true,
            },
        };

    private static string WriteInternalAudit95PromotionSource(ReadinessRegisterPromotionPaths paths)
    {
        var movementSpecs = new (string FeatureId, string DimensionId, int TargetScore, string BlockerId, string[] Gates, string SourceGapRow, string EvidenceId)[]
        {
            ("FEAT-157", "RDY-DIM-001", 10, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM001-001", ["AT-RDY-001"], "Protocol/evidence architecture", "RDY-EVID-AT-RDY-001-FEAT-157-001"),
            ("FEAT-158", "RDY-DIM-002", 10, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM002-001", ["AT-RDY-007"], "Verifier/sample/tamper corpus", "RDY-EVID-AT-RDY-007-FEAT-158-001"),
            ("FEAT-159", "RDY-DIM-003", 10, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM003-001", ["AT-RDY-008"], "Cross-device receipt/inclusion verification", "RDY-EVID-AT-RDY-008-FEAT-159-001"),
            ("FEAT-160", "RDY-DIM-004", 10, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM004-001", ["AT-RDY-001"], "Protocol/evidence architecture", "RDY-EVID-AT-RDY-001-FEAT-160-001"),
            ("FEAT-161", "RDY-DIM-005", 9, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM005-001", ["AT-RDY-002", "AT-RDY-003", "AT-RDY-004"], "Per-election KMS custody lifecycle", "RDY-EVID-AT-RDY-002-FEAT-161-001"),
            ("FEAT-162", "RDY-DIM-006", 9, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM006-001", ["AT-RDY-005"], "Trusted deployment ceremony", "RDY-EVID-AT-RDY-005-FEAT-162-001"),
            ("FEAT-163", "RDY-DIM-007", 10, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM007-001", ["AT-RDY-006", "AT-RDY-014"], "Operational readiness package", "RDY-EVID-AT-RDY-006-FEAT-163-001"),
            ("FEAT-164", "RDY-DIM-008", 9, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM008-001", ["AT-RDY-009", "AT-RDY-010"], "Retention/log privacy proof", "RDY-EVID-AT-RDY-009-FEAT-164-001"),
            ("FEAT-165", "RDY-DIM-009", 9, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM009-001", ["AT-RDY-011", "AT-RDY-015"], "Dispute/continuity readiness", "RDY-EVID-AT-RDY-011-FEAT-165-001"),
            ("FEAT-166", "RDY-DIM-010", 9, "RDY-BLOCK-INTERNAL_AUDIT_95_DIM010-001", ["AT-RDY-012", "AT-RDY-013", "AT-RDY-014"], "Legal/governance boundary wrapper", "RDY-EVID-AT-RDY-012-FEAT-166-001"),
        };
        var movements = new JsonArray();
        string? firstArtifact = null;
        foreach (var spec in movementSpecs)
        {
            var relativePath = $"unit-test/internal-audit-95/{spec.FeatureId}-score-proposal.json";
            var fullPath = Path.Combine(paths.WorkspaceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.WriteAllText(
                fullPath,
                $$"""
                {"featureId":"{{spec.FeatureId}}","dimensionId":"{{spec.DimensionId}}","from":8,"to":{{spec.TargetScore}}}
                """);
            firstArtifact ??= fullPath;
            movements.Add(CreateInternalAudit95Movement(
                spec.FeatureId,
                spec.DimensionId,
                spec.TargetScore,
                spec.BlockerId,
                spec.Gates,
                spec.SourceGapRow,
                spec.EvidenceId,
                relativePath,
                ComputeSha256Hex(File.ReadAllBytes(fullPath))));
        }

        var source = new JsonObject
        {
            ["schemaVersion"] = "internal-audit-95-promotion-source.v1",
            ["sourceId"] = "IA95-PROMOTION-20260604-001",
            ["featureId"] = "FEAT-130",
            ["status"] = "accepted",
            ["generatedAt"] = "2026-06-04T12:00:00Z",
            ["baselineRegister"] = new JsonObject
            {
                ["registerVersionId"] = "RDY-REG-v0.1.7",
                ["registerVersion"] = "v0.1.7",
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 80,
                ["strongestAllowedClaim"] = "friendly_organization_pilot",
                ["publicationStatus"] = "pilot_only_with_limitations",
            },
            ["targetRegister"] = new JsonObject
            {
                ["registerVersionId"] = "RDY-REG-v0.1.8",
                ["registerVersion"] = "v0.1.8",
                ["status"] = "AcceptedInternal",
                ["totalScore"] = 95,
                ["strongestAllowedClaim"] = "friendly_organization_pilot",
                ["publicationStatus"] = "pilot_only_with_limitations",
                ["productionFutureGateWording"] = "Production organizational rollout remains a downstream execution gate after internal-audit-95 hardening; local rehearsal, binding-election proof generation, proof verification, customer governance, and external review must be accepted before claiming rollout readiness.",
                ["publicStateExternalBoundaryWording"] = "Public or state election readiness is a downstream external boundary; Hush can prepare technical evidence, but authority, jurisdiction, certification, transparency, accessibility, procurement, and dispute-remedy prerequisites sit outside this internal audit report.",
            },
            ["scoreMovements"] = movements,
        };

        var sourcePath = Path.Combine(
            paths.WorkspaceRoot,
            "hush-documents",
            "PrivateServer_ElectronicVoting",
            "Internal-Audit-95-Promotion-Register",
            "package",
            "internal-audit-95-promotion-source.json");
        Directory.CreateDirectory(Path.GetDirectoryName(sourcePath)!);
        File.WriteAllText(sourcePath, source.ToJsonString(JsonOptions));
        return firstArtifact!;
    }

    private static JsonObject CreateInternalAudit95Movement(
        string featureId,
        string dimensionId,
        int acceptedScore,
        string blockerId,
        IReadOnlyList<string> gates,
        string sourceGapRow,
        string evidenceId,
        string artifactRelativePath,
        string artifactHash) =>
        new()
        {
            ["movementId"] = $"IA95-SCORE-{dimensionId}-{featureId}",
            ["featureId"] = featureId,
            ["dimensionId"] = dimensionId,
            ["previousScore"] = 8,
            ["acceptedScore"] = acceptedScore,
            ["delta"] = acceptedScore - 8,
            ["status"] = "accepted",
            ["freshness"] = "current",
            ["directRegisterMutation"] = false,
            ["targetBlockerId"] = blockerId,
            ["acceptanceGateIds"] = ToJsonArray(gates),
            ["sourceGapRows"] = new JsonArray(sourceGapRow),
            ["evidenceIds"] = new JsonArray(evidenceId),
            ["artifactRefs"] = new JsonArray(
                new JsonObject
                {
                    ["artifactId"] = $"{featureId}-SCORE-PROPOSAL",
                    ["path"] = artifactRelativePath,
                    ["sha256Hash"] = artifactHash,
                    ["visibility"] = "restricted",
                }),
            ["claimEffect"] = $"Raises {dimensionId} evidence to {acceptedScore} for the RDY-REG-v0.1.8 internal-audit-95 promotion.",
            ["residualRisk"] = $"{featureId} residual risk remains bounded by downstream execution and external-boundary wording.",
            ["resolutionCriteria"] = $"{featureId} accepted score proposal was consumed by RDY-REG-v0.1.8 and resolves {blockerId}.",
        };

    private static JsonArray ToJsonArray(IEnumerable<string> values)
    {
        var result = new JsonArray();
        foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
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
                    "Current go/no-go result: controlled friendly-organization pilot planning is allowed with limitations; production rollout is future-gated by the 95+ hardening plan and public/state election readiness remains an external boundary.",
                    "internal_non_binding_rehearsal",
                    "allowed_with_limitations"),
                ["scorecardForbiddenPhrases"] = new JsonArray(
                    "Current go/no-go result: internal non-binding rehearsal is allowed with limitations; pilot and stronger claims are blocked."),
                ["restrictedRequiredPhrases"] = new JsonArray(
                    "friendly_organization_pilot",
                    "production_organizational_rollout"),
                ["publicSafeRequiredPhrases"] = new JsonArray(
                    "HushVoting may be discussed for controlled friendly-organization pilot use with explicit limitations.",
                    "Production rollout is a future execution gate after Hush-owned 95+ hardening is complete.",
                    "Public/state election readiness is an external boundary outside this internal audit report."),
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

    private static string GetFeat156ReviewerPackageRoot(ReadinessRegisterPromotionPaths paths) =>
        Path.Combine(
            paths.WorkspaceRoot,
            "hush-documents",
            "PrivateServer_ElectronicVoting",
            "Production-Rollout-Promotion-Register",
            "package");

    private static JsonObject FindClaim(JsonObject register, string claimLevel) =>
        register["claimLevels"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(claim => claim["claimLevel"]!.GetValue<string>() == claimLevel);

    private static JsonObject FindDimension(JsonObject register, string dimensionId) =>
        register["dimensions"]!.AsArray()
            .Select(node => node!.AsObject())
            .Single(dimension => dimension["dimensionId"]!.GetValue<string>() == dimensionId);

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
