using System.Text.Json;
using System.Text.Json.Nodes;
using FailedFinalizeContinuityRehearsalPromoter;
using FluentAssertions;
using Xunit;

namespace HushServerNode.Tests.Elections;

public sealed class FailedFinalizeContinuityGateCheckerTests
{
    [Fact]
    public void ReleaseBaselineSource_EvaluatesAcceptedAndCanGenerateScoreProposal()
    {
        var source = LoadBaseline();

        var sourceErrors = FailedFinalizeContinuityContracts.ValidateSource(source);
        var evaluation = FailedFinalizeContinuityGateChecker.Evaluate(source);

        sourceErrors.Should().BeEmpty();
        evaluation.Status.Should().Be("accepted");
        evaluation.ScoreProposalCanBeGenerated.Should().BeTrue();
        evaluation.ScoreChangeAllowed.Should().BeTrue();
        evaluation.DirectRegisterMutation.Should().BeFalse();
        evaluation.DownstreamHandoffStatus.Should().Be("accepted");
        evaluation.Blockers.Should().BeEmpty();
    }

    [Fact]
    public void MissingFailedFinalizeDecision_BlocksWithDecisionDiagnostic()
    {
        var source = Clone(LoadBaseline());
        var outcome = source["governedOutcome"]!.AsObject();
        outcome["authorityDecisionRef"] = "";
        outcome["authorityDecisionHash"] = "";

        var evaluation = FailedFinalizeContinuityGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.MissingDecisionBlocker);
        evaluation.ScoreProposalCanBeGenerated.Should().BeFalse();
    }

    [Fact]
    public void MissingFailedFinalizeEvidence_BlocksAndDoesNotTreatFeat154ContextAsEnough()
    {
        var source = Clone(LoadBaseline());
        var outcome = source["governedOutcome"]!.AsObject();
        outcome["missingFinalizeEvidenceRefs"] = new JsonArray();
        outcome["continuityEvidenceRefs"] = new JsonArray();
        outcome["availableTrusteeAcknowledgementRefs"] = new JsonArray();

        var evaluation = FailedFinalizeContinuityGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.MissingEvidenceBlocker);
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.Feat154ContextOnlyBlocker);
    }

    [Fact]
    public void Feat146EvidenceReuse_BlocksFailedFinalizeClaim()
    {
        var source = Clone(LoadBaseline());
        var outcome = source["governedOutcome"]!.AsObject();
        outcome["decisionType"] = "finalized_with_anomaly";
        outcome["continuityEvidenceRefs"] = new JsonArray("FEAT-146:governed-outcome-producer");

        var evaluation = FailedFinalizeContinuityGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.Feat146ReuseBlocker);
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.MissingDecisionBlocker);
    }

    [Fact]
    public void CleanResultArtifactConflict_BlocksPackage()
    {
        var source = Clone(LoadBaseline());
        source["noCleanResult"]!.AsObject()["officialResultArtifactPresent"] = true;
        source["governedOutcome"]!.AsObject()["officialResultRef"] = "election-result:official";

        var evaluation = FailedFinalizeContinuityGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.CleanResultConflictBlocker);
    }

    [Fact]
    public void DirectRegisterMutation_BlocksPromotionInput()
    {
        var source = Clone(LoadBaseline());
        source["readinessProposal"]!.AsObject()["directRegisterMutation"] = true;

        var evaluation = FailedFinalizeContinuityGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.DirectRegisterMutation.Should().BeTrue();
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.DirectRegisterMutationBlocker);
    }

    [Fact]
    public void PublicForbiddenMaterial_BlocksPublicSafety()
    {
        var source = Clone(LoadBaseline());
        var publicSamples = source["publicArtifactSamples"]!.AsArray();
        publicSamples[0]!.AsObject()["content"] =
            "Public status leaked a private key and trustee secret by mistake.";

        var evaluation = FailedFinalizeContinuityGateChecker.Evaluate(source);

        evaluation.Status.Should().Be("blocked");
        evaluation.Blockers.Should().Contain(FailedFinalizeContinuityGateChecker.PublicSafetyBlocker);
    }

    [Fact]
    public void ReviewerOutputs_PublicSummaryContainsRequiredNonClaimsAndNoRestrictedMaterial()
    {
        var source = LoadBaseline();

        var outputs = FailedFinalizeContinuityReviewerOutputGenerator.Generate(
            source,
            changedFiles:
            [
                "hush-server-node/Node/scripts/FailedFinalizeContinuityRehearsalPromoter/FailedFinalizeContinuityReviewerOutputs.cs",
                "hush-memory-bank/Features/03_IN_PROGRESS/FEAT-155-failed-finalize-continuity-rehearsal/FeatureTasks.md",
            ]);

        outputs.PublicSafetyScan.Passed.Should().BeTrue();
        outputs.PublicSafeSummary.Should().Contain("No valid official result exists");
        outputs.PublicSafeSummary.Should().Contain("Legal remedy sufficiency is not claimed");
        outputs.PublicSafeSummary.Should().Contain("Production organizational rollout readiness is not claimed");
        outputs.PublicSafeSummary.Should().NotContain("trustee secret");
        outputs.PublicSafeSummary.Should().NotContain("vote choice");
        outputs.NoUiBoundary.Status.Should().Be("confirmed");
        outputs.NoUiBoundary.HasUiChanges.Should().BeFalse();

        var restrictedIndex = JsonSerializer.Deserialize<FailedFinalizeRestrictedEvidenceIndexRecord>(
            outputs.RestrictedEvidenceIndexJson,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        restrictedIndex.Visibility.Should().Be("restricted_owner_auditor");
        restrictedIndex.Entries.Should().ContainSingle(entry =>
            entry.EvidenceId == "FEAT155-RESTRICTED-CONTINUITY-INDEX" &&
            entry.Purpose.Length > 0 &&
            entry.Visibility == "restricted_owner_auditor" &&
            entry.Sha256Hash.Length > 0);
    }

    [Fact]
    public void ReviewerOutputs_PublicSafetyScanRejectsRestrictedMaterial()
    {
        var scan = FailedFinalizeContinuityReviewerOutputGenerator.ScanPublicText(
            "Public status leaked a trustee secret and voter address.");

        scan.Passed.Should().BeFalse();
        scan.Findings.Should().Contain(x => x.Contains("trustee secret", StringComparison.OrdinalIgnoreCase));
        scan.Findings.Should().Contain(x => x.Contains("voter address", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void NoUiBoundaryChecker_BlocksHushWebClientChangesWithoutDesign()
    {
        var evaluation = FailedFinalizeContinuityReviewerOutputGenerator.EvaluateNoUiBoundary(
            [
                "hush-web-client/src/app/elections/failed-finalize/page.tsx",
                "hush-server-node/Node/scripts/FailedFinalizeContinuityRehearsalPromoter/FailedFinalizeContinuityReviewerOutputs.cs",
            ]);

        evaluation.Status.Should().Be("blocked");
        evaluation.HasUiChanges.Should().BeTrue();
        evaluation.ChangedUiFiles.Should().ContainSingle(path =>
            path == "hush-web-client/src/app/elections/failed-finalize/page.tsx");
    }

    [Fact]
    public void PromotionService_ValidateOnly_DoesNotWritePackageOutputs()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var paths = CreatePromotionPaths(tempRoot);

            var result = new FailedFinalizeContinuityPromotionService().Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModeValidateOnly,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: FixedGeneratedAt,
                ValidateOnly: false));

            result.Mode.Should().Be(FailedFinalizeContinuityPromotionService.ModeValidateOnly);
            result.Status.Should().Be("accepted");
            result.WrittenFiles.Should().BeEmpty();
            result.CheckedFiles.Should().BeEmpty();
            Directory.Exists(result.PackageRoot).Should().BeFalse();
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void PromotionService_PackageThenCheckOnly_ValidatesDeterministicOutputs()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var paths = CreatePromotionPaths(tempRoot);
            var service = new FailedFinalizeContinuityPromotionService();

            var packageResult = service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModePackage,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: FixedGeneratedAt,
                ValidateOnly: false));
            var checkResult = service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModeCheckOnly,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: null,
                ValidateOnly: false));

            packageResult.WrittenFiles.Should().HaveCount(FailedFinalizeContinuityArtifactGenerator.RequiredArtifactPaths.Length);
            checkResult.CheckedFiles.Should().HaveCount(FailedFinalizeContinuityArtifactGenerator.RequiredArtifactPaths.Length);
            packageResult.GeneratedPackage.Artifacts.Select(artifact => artifact.Sha256Hash)
                .Should()
                .Equal(checkResult.GeneratedPackage.Artifacts.Select(artifact => artifact.Sha256Hash));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void PromotionService_CheckOnlyDetectsTamperedOutput()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var paths = CreatePromotionPaths(tempRoot);
            var service = new FailedFinalizeContinuityPromotionService();
            var packageResult = service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModePackage,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: FixedGeneratedAt,
                ValidateOnly: false));
            var publicSummaryPath = Path.Combine(
                packageResult.PackageRoot,
                FailedFinalizeContinuityArtifactGenerator.PublicSafeSummaryPath);
            File.AppendAllText(publicSummaryPath, "tampered");

            var act = () => service.Promote(new(
                paths,
                FailedFinalizeContinuityPromotionService.ModeCheckOnly,
                SourceInput: null,
                OutputRoot: null,
                GeneratedAt: null,
                ValidateOnly: false));

            act.Should()
                .Throw<FailedFinalizeContinuityPromotionException>()
                .Where(ex => ex.Details.Any(detail => detail.Contains("Hash mismatch", StringComparison.Ordinal)));
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void ArtifactGenerator_DownstreamHandoffContainsFeat156Inputs()
    {
        var source = LoadBaseline();

        var package = FailedFinalizeContinuityArtifactGenerator.GenerateFromSource(source, FixedGeneratedAt);

        package.Status.Should().Be("accepted");
        var handoffArtifact = package.Artifacts.Should()
            .ContainSingle(artifact => artifact.RelativePath == FailedFinalizeContinuityArtifactGenerator.DownstreamHandoffPath)
            .Subject;
        var handoff = JsonNode.Parse(handoffArtifact.Content)!.AsObject();
        FailedFinalizeContinuityContracts.GetString(handoff, "status").Should().Be("accepted");
        FailedFinalizeContinuityContracts.GetBool(handoff, "directRegisterMutation", fallback: true).Should().BeFalse();
        FailedFinalizeContinuityContracts.GetStringArray(handoff, "consumers").Should().Contain(["FEAT-148", "FEAT-156"]);
        FailedFinalizeContinuityContracts.GetString(handoff, "scoreProposalPath")
            .Should()
            .Be(FailedFinalizeContinuityArtifactGenerator.ScoreProposalPath);
        FailedFinalizeContinuityContracts.GetString(handoff, "readinessFragmentPath")
            .Should()
            .Be(FailedFinalizeContinuityArtifactGenerator.ReadinessFragmentPath);
        handoff["failedFinalizeBlockerClearance"]!.AsObject()["blockersCleared"]!.AsArray()
            .Select(item => item!.GetValue<string>())
            .Should()
            .Contain("FEAT139-GOVERNED-FAILED-FINALIZE-MISSING");
    }

    private static JsonObject LoadBaseline() =>
        FailedFinalizeContinuityContracts.ReadJsonObject(
            Path.Combine(SourceRoot, "failed-finalize-continuity-source.json"));

    private static JsonObject Clone(JsonObject source) =>
        JsonNode.Parse(source.ToJsonString(new JsonSerializerOptions(JsonSerializerDefaults.Web)))!.AsObject();

    private static string FixtureRoot =>
        Path.Combine(
            HushVotingReadinessTestArtifacts.ServerNodeRoot,
            "Node",
            "HushServerNode.Tests",
            "Fixtures",
            "HushVotingReadiness",
            "Failed-Finalize-Continuity-Rehearsal");

    private static string SourceRoot => Path.Combine(FixtureRoot, "examples", "release-baseline");

    private static DateTimeOffset FixedGeneratedAt =>
        DateTimeOffset.Parse("2026-05-31T00:00:00Z");

    private static FailedFinalizeContinuityPromotionPaths CreatePromotionPaths(string tempRoot) =>
        new(
            tempRoot,
            FixtureRoot,
            Path.Combine(FixtureRoot, "schemas"),
            Path.Combine(FixtureRoot, "examples"),
            Path.Combine(SourceRoot, "failed-finalize-continuity-source.json"),
            Path.Combine(tempRoot, "Failed-Finalize-Continuity-Rehearsal"));

    private static string CreateTempRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "hush-feat155-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteTempRoot(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }
}
