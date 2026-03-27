using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionBoundaryAndTrusteeRecordTests
{
    [Fact]
    public void CreateBoundaryArtifact_CapturesFrozenPolicyAndPlaceholders()
    {
        var election = CreateTrusteeThresholdElection();
        var trusteeSnapshot = ElectionModelFactory.CreateTrusteeBoundarySnapshot(
            requiredApprovalCount: 2,
            acceptedTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
            ]);
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            ceremonyVersionId: Guid.NewGuid(),
            ceremonyVersionNumber: 3,
            profileId: "dkg-prod-2of2",
            boundTrusteeCount: 2,
            requiredApprovalCount: 2,
            activeTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
            ],
            tallyPublicKeyFingerprint: "tally-fingerprint-1");
        var eligibleHash = new byte[] { 1, 2, 3, 4 };
        var acceptedBallotHash = new byte[] { 5, 6, 7, 8 };

        var artifact = ElectionModelFactory.CreateBoundaryArtifact(
            artifactType: ElectionBoundaryArtifactType.Open,
            election: election,
            recordedByPublicAddress: "owner-address",
            trusteeSnapshot: trusteeSnapshot,
            ceremonySnapshot: ceremonySnapshot,
            frozenEligibleVoterSetHash: eligibleHash,
            trusteePolicyExecutionReference: "governance-placeholder",
            acceptedBallotSetHash: acceptedBallotHash);

        artifact.ArtifactType.Should().Be(ElectionBoundaryArtifactType.Open);
        artifact.LifecycleState.Should().Be(ElectionLifecycleState.Open);
        artifact.Metadata.OwnerPublicAddress.Should().Be("owner-address");
        artifact.Policy.RequiredApprovalCount.Should().Be(2);
        artifact.TrusteeSnapshot.Should().NotBeNull();
        artifact.TrusteeSnapshot!.EveryAcceptedTrusteeMustApprove.Should().BeTrue();
        artifact.CeremonySnapshot.Should().NotBeNull();
        artifact.CeremonySnapshot!.ProfileId.Should().Be("dkg-prod-2of2");
        artifact.CeremonySnapshot.TallyPublicKeyFingerprint.Should().Be("tally-fingerprint-1");
        artifact.FrozenEligibleVoterSetHash.Should().Equal(eligibleHash);
        artifact.AcceptedBallotSetHash.Should().Equal(acceptedBallotHash);
        artifact.Options.Last().OptionId.Should().Be(ElectionOptionDefinition.ReservedBlankOptionId);
    }

    [Fact]
    public void TrusteeInvitation_AcceptFromDraft_PreservesExplicitState()
    {
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            electionId: ElectionId.NewElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: 2);

        var accepted = invitation.Accept(
            respondedAt: DateTime.UtcNow,
            resolvedAtDraftRevision: 3,
            lifecycleState: ElectionLifecycleState.Draft);

        accepted.Status.Should().Be(ElectionTrusteeInvitationStatus.Accepted);
        accepted.ResolvedAtDraftRevision.Should().Be(3);
        accepted.RespondedAt.Should().NotBeNull();
        accepted.RevokedAt.Should().BeNull();
    }

    [Fact]
    public void TrusteeInvitation_AcceptAfterOpen_ShouldThrow()
    {
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            electionId: ElectionId.NewElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: 1);

        var act = () => invitation.Accept(
            respondedAt: DateTime.UtcNow,
            resolvedAtDraftRevision: 2,
            lifecycleState: ElectionLifecycleState.Open);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*only valid while the election remains in draft*");
    }

    [Fact]
    public void TrusteeInvitation_AcceptWithEarlierResolvedRevision_ShouldThrow()
    {
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            electionId: ElectionId.NewElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: 3);

        var act = () => invitation.Accept(
            respondedAt: DateTime.UtcNow,
            resolvedAtDraftRevision: 2,
            lifecycleState: ElectionLifecycleState.Draft);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*cannot resolve before the sent draft revision*");
    }

    [Fact]
    public void TrusteeInvitation_RevokeAfterPendingDraft_ShouldUpdateState()
    {
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            electionId: ElectionId.NewElectionId,
            trusteeUserAddress: "trustee-b",
            trusteeDisplayName: "Bob",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: 1);

        var revoked = invitation.Revoke(
            revokedAt: DateTime.UtcNow,
            resolvedAtDraftRevision: 2,
            lifecycleState: ElectionLifecycleState.Draft);

        revoked.Status.Should().Be(ElectionTrusteeInvitationStatus.Revoked);
        revoked.ResolvedAtDraftRevision.Should().Be(2);
        revoked.RevokedAt.Should().NotBeNull();
        revoked.RespondedAt.Should().BeNull();
    }

    [Fact]
    public void CreateTrusteeBoundarySnapshot_WithDuplicateTrustees_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateTrusteeBoundarySnapshot(
            requiredApprovalCount: 1,
            acceptedTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("TRUSTEE-A", "Alice Again"),
            ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*Duplicate accepted trustee*");
    }

    [Fact]
    public void CreateTrusteeBoundarySnapshot_WithRequiredApprovalCountGreaterThanAcceptedTrustees_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateTrusteeBoundarySnapshot(
            requiredApprovalCount: 3,
            acceptedTrustees:
            [
                new ElectionTrusteeReference("trustee-a", "Alice"),
                new ElectionTrusteeReference("trustee-b", "Bob"),
            ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*cannot exceed the accepted trustee count*");
    }

    private static ElectionRecord CreateTrusteeThresholdElection() =>
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
