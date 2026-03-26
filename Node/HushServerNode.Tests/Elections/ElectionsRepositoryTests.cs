using FluentAssertions;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionsRepositoryTests
{
    [Fact]
    public async Task SaveElectionAndDraftSnapshot_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateAdminElection();
        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            snapshotReason: "initial draft",
            recordedByPublicAddress: "owner-address");

        await repository.SaveElectionAsync(election);
        await repository.SaveDraftSnapshotAsync(snapshot);
        await context.SaveChangesAsync();

        var retrievedElection = await repository.GetElectionAsync(election.ElectionId);
        var latestSnapshot = await repository.GetLatestDraftSnapshotAsync(election.ElectionId);

        retrievedElection.Should().NotBeNull();
        retrievedElection!.Options.Last().OptionId.Should().Be(ElectionOptionDefinition.ReservedBlankOptionId);
        latestSnapshot.Should().NotBeNull();
        latestSnapshot!.SnapshotReason.Should().Be("initial draft");
        latestSnapshot.Policy.ProtocolOmegaVersion.Should().Be("omega-v1.0.0");
    }

    [Fact]
    public async Task SaveArtifactsWarningsAndInvitations_ShouldRoundTrip()
    {
        using var context = CreateContext();
        var repository = CreateRepository(context);
        var election = CreateTrusteeElection();
        var warning = ElectionModelFactory.CreateWarningAcknowledgement(
            electionId: election.ElectionId,
            warningCode: ElectionWarningCode.LowAnonymitySet,
            draftRevision: election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            electionId: election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);
        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType: ElectionBoundaryArtifactType.Open,
            election: election,
            recordedByPublicAddress: "owner-address",
            trusteeSnapshot: ElectionModelFactory.CreateTrusteeBoundarySnapshot(
                requiredApprovalCount: 2,
                acceptedTrustees:
                [
                    new ElectionTrusteeReference("trustee-a", "Alice"),
                    new ElectionTrusteeReference("trustee-b", "Bob"),
                ]));

        await repository.SaveElectionAsync(election);
        await repository.SaveWarningAcknowledgementAsync(warning);
        await repository.SaveTrusteeInvitationAsync(invitation);
        await repository.SaveBoundaryArtifactAsync(artifact);
        await context.SaveChangesAsync();

        var acceptedInvitation = invitation.Accept(
            respondedAt: DateTime.UtcNow,
            resolvedAtDraftRevision: election.CurrentDraftRevision,
            lifecycleState: ElectionLifecycleState.Draft);
        await repository.UpdateTrusteeInvitationAsync(acceptedInvitation);
        await context.SaveChangesAsync();

        var warnings = await repository.GetWarningAcknowledgementsAsync(election.ElectionId);
        var invitations = await repository.GetTrusteeInvitationsAsync(election.ElectionId);
        var artifacts = await repository.GetBoundaryArtifactsAsync(election.ElectionId);

        warnings.Should().ContainSingle();
        warnings[0].WarningCode.Should().Be(ElectionWarningCode.LowAnonymitySet);
        invitations.Should().ContainSingle();
        invitations[0].Status.Should().Be(ElectionTrusteeInvitationStatus.Accepted);
        artifacts.Should().ContainSingle();
        artifacts[0].ArtifactType.Should().Be(ElectionBoundaryArtifactType.Open);
    }

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

    private static ElectionRecord CreateAdminElection() =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Board Election",
            shortDescription: "Annual board vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "ORG-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 2, IsBlankOption: false),
            ]);

    private static ElectionRecord CreateTrusteeElection() =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Referendum",
            shortDescription: "Policy vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "REF-1",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.TrusteeThreshold,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.PassFail,
                "pass_fail_yes_no",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: true,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "simple_majority_of_non_blank_votes"),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("no", "No", null, 2, IsBlankOption: false),
            ],
            requiredApprovalCount: 2);
}
