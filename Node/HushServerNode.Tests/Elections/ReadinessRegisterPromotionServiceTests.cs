using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using ReadinessRegisterPromoter;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class ReadinessRegisterPromotionServiceTests
{
    private static readonly DateTimeOffset FixedGeneratedAt = new(2026, 5, 19, 0, 0, 0, TimeSpan.Zero);
    private const string CurrentRegisterVersion = "v0.1.1";
    private const string CurrentRegisterVersionId = "RDY-REG-v0.1.1";
    private const int CurrentTotalScore = 55;

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
        scorecard.Should().Contain("RDY-BLOCK-FRIENDLY_ORGANIZATION_PILOT-001");
        scorecard.Should().Contain("green | resolved | FEAT-131");
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
        bool validateOnly = false) =>
        new(
            paths,
            "hushvoting-readiness-register",
            CurrentRegisterVersion,
            "not_for_publication",
            validateOnly,
            Scaffold: false,
            FixedGeneratedAt);

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
