using FluentAssertions;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionGovernedOutcomeRepositoryTests
{
    [Fact]
    public async Task SaveGovernedOutcomeAndKeyLostContinuityDecisions_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var decidedAt = DateTime.UtcNow.AddMinutes(-2);
        var recordedAt = DateTime.UtcNow.AddMinutes(-1);
        var keyLostDecision = CreateKeyLostDecision(
            electionId,
            " trustee-one ",
            decidedAt,
            recordedAt);
        var outcomeDecision = CreateGovernedOutcomeDecision(
            electionId,
            keyLostDecision.Id,
            decidedAt.AddSeconds(30),
            recordedAt.AddSeconds(30));

        await repository.SaveTrusteeContinuityDecisionAsync(keyLostDecision);
        await repository.SaveGovernedOutcomeDecisionAsync(outcomeDecision);
        await context.SaveChangesAsync();

        var continuityDecisions = await repository.GetTrusteeContinuityDecisionsAsync(electionId);
        var keyLostDecisions = await repository.GetKeyLostTrusteeContinuityDecisionsAsync(electionId);
        var currentTrusteeDecision = await repository.GetCurrentTrusteeContinuityDecisionAsync(
            electionId,
            "trustee-one");
        var storedOutcome = await repository.GetLatestGovernedOutcomeDecisionAsync(electionId);

        continuityDecisions.Should().ContainSingle();
        keyLostDecisions.Should().ContainSingle();
        currentTrusteeDecision.Should().NotBeNull();
        currentTrusteeDecision!.TrusteePublicAddress.Should().Be("trustee-one");
        currentTrusteeDecision.BlocksThresholdActions.Should().BeTrue();
        currentTrusteeDecision.ContinuityEvidenceRefs.Should().Equal("incident:key-lost:1");

        storedOutcome.Should().NotBeNull();
        storedOutcome!.OutcomeStatus.Should().Be(ElectionOutcomeStatus.FinalizedWithAnomaly);
        storedOutcome.CleanFinalization.Should().BeFalse();
        storedOutcome.ResultingLifecycleState.Should().Be(ElectionLifecycleState.Finalized);
        storedOutcome.KeyLostTrusteeDecisionIds.Should().Equal(keyLostDecision.Id);
        storedOutcome.MissingFinalizeEvidenceRefs.Should().Equal("missing:clean-threshold-finalize");
        storedOutcome.HasAbnormalOutcomeEvidence.Should().BeTrue();

        var byId = await repository.GetGovernedOutcomeDecisionAsync(outcomeDecision.Id);
        byId.Should().BeEquivalentTo(storedOutcome);
    }

    [Fact]
    public async Task SaveGovernedOutcomeDecision_WhenElectionAlreadyHasDecision_ShouldFailClosed()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var firstDecision = CreateGovernedOutcomeDecision(
            electionId,
            keyLostDecisionId: Guid.NewGuid(),
            DateTime.UtcNow.AddMinutes(-2),
            DateTime.UtcNow.AddMinutes(-1));
        var duplicateDecision = CreateGovernedOutcomeDecision(
            electionId,
            keyLostDecisionId: Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow);

        await repository.SaveGovernedOutcomeDecisionAsync(firstDecision);
        await context.SaveChangesAsync();

        var act = () => repository.SaveGovernedOutcomeDecisionAsync(duplicateDecision);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A governed outcome decision already exists for this election.");
    }

    [Fact]
    public async Task SaveTrusteeContinuityDecision_WhenTrusteeAlreadyKeyLost_ShouldFailClosed()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var electionId = ElectionId.NewElectionId;
        var firstDecision = CreateKeyLostDecision(electionId, "trustee-one", DateTime.UtcNow, DateTime.UtcNow);
        var duplicateDecision = CreateKeyLostDecision(electionId, "trustee-one", DateTime.UtcNow, DateTime.UtcNow);

        await repository.SaveTrusteeContinuityDecisionAsync(firstDecision);
        await context.SaveChangesAsync();

        var act = () => repository.SaveTrusteeContinuityDecisionAsync(duplicateDecision);

        await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("A trustee continuity decision already exists for this election, trustee, and status.");
    }

    [Fact]
    public void GovernedOutcomeDecision_WhenAbnormalDecisionClaimsCleanFinalization_ShouldThrow()
    {
        var act = () => CreateGovernedOutcomeDecision(
            ElectionId.NewElectionId,
            keyLostDecisionId: Guid.NewGuid(),
            DateTime.UtcNow,
            DateTime.UtcNow,
            cleanFinalization: true);

        act.Should()
            .Throw<ArgumentException>()
            .WithMessage("Abnormal governed outcome decisions cannot claim clean finalization.*");
    }

    private static ElectionGovernedOutcomeDecisionRecord CreateGovernedOutcomeDecision(
        ElectionId electionId,
        Guid keyLostDecisionId,
        DateTime decidedAt,
        DateTime recordedAt,
        bool cleanFinalization = false) =>
        new(
            Guid.NewGuid(),
            electionId,
            ElectionGovernedOutcomeDecisionType.AcceptFixedUnofficialResultWithAnomaly,
            ElectionOutcomeStatus.FinalizedWithAnomaly,
            cleanFinalization,
            ElectionGovernedOutcomeFinalizationMode.AbnormalFinalization,
            ElectionLifecycleState.Closed,
            ElectionLifecycleState.Finalized,
            " owner-address ",
            "ElectionOwner",
            "FEAT-140",
            "hush-documents/PrivateServer_ElectronicVoting/Legal-Governance-Boundary/package/legal-governance-boundary-feat146-handoff.json",
            "3802773c78d2a0d49822c3823dad65c65be88747f6270c1e4ce68a849328cd78",
            "governance:decision:accept-fixed-result",
            "authority-hash",
            "governance-rule:abnormal-finalization-v1",
            "finality-rule:fixed-result-copy-v1",
            "remedy-rule:key-lost-v1",
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            ["missing:clean-threshold-finalize"],
            ["incident:key-lost:1"],
            ["ack:available-trustee:1"],
            [keyLostDecisionId],
            "Fixed tally-ready result accepted with explicit abnormal-finalization disclosure.",
            decidedAt,
            recordedAt,
            Guid.NewGuid(),
            42,
            Guid.NewGuid());

    private static ElectionTrusteeContinuityDecisionRecord CreateKeyLostDecision(
        ElectionId electionId,
        string trusteePublicAddress,
        DateTime decidedAt,
        DateTime recordedAt) =>
        new(
            Guid.NewGuid(),
            electionId,
            trusteePublicAddress,
            "Trustee One",
            ElectionTrusteeContinuityStatus.KeyLost,
            "governance:decision:key-lost",
            "continuity-authority-hash",
            "governance-rule:trustee-continuity-v1",
            ["incident:key-lost:1"],
            "owner-address",
            decidedAt,
            recordedAt,
            Guid.NewGuid(),
            41,
            Guid.NewGuid());

    private static ElectionsRepository CreateRepository(ElectionsDbContext context)
    {
        var repository = new ElectionsRepository();
        repository.SetContext(context);
        return repository;
    }

    private static ElectionsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ElectionsDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        return new ElectionsDbContext(new ElectionsDbContextConfigurator(), options);
    }
}
