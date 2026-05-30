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
}
