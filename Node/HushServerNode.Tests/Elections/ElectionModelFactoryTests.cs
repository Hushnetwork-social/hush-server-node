using FluentAssertions;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionModelFactoryTests
{
    [Fact]
    public void CreateDraftRecord_AppendsReservedBlankVoteLast_AndRetainsOwnedFields()
    {
        var election = ElectionModelFactory.CreateDraftRecord(
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
            outcomeRule: CreateSingleWinnerRule(),
            approvedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
                new ApprovedClientApplicationRecord("event-app", "2.4.1"),
            ],
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 10, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", "Incumbent", 20, IsBlankOption: false),
            ],
            acknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
                ElectionWarningCode.LowAnonymitySet,
            ]);

        election.LifecycleState.Should().Be(ElectionLifecycleState.Draft);
        election.Title.Should().Be("Board Election");
        election.ExternalReferenceCode.Should().Be("ORG-2026-01");
        election.RequiredApprovalCount.Should().BeNull();
        election.Options.Should().HaveCount(3);
        election.Options[0].OptionId.Should().Be("alice");
        election.Options[1].OptionId.Should().Be("bob");
        election.Options[2].OptionId.Should().Be(ElectionOptionDefinition.ReservedBlankOptionId);
        election.Options[2].DisplayLabel.Should().Be(ElectionOptionDefinition.ReservedBlankOptionLabel);
        election.Options[2].IsBlankOption.Should().BeTrue();
        election.Options[2].BallotOrder.Should().Be(21);
        election.AcknowledgedWarningCodes.Should().Equal(ElectionWarningCode.LowAnonymitySet);
    }

    [Fact]
    public void CreateDraftRecord_WithOwnerManagedBlankOption_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Board Election",
            shortDescription: null,
            ownerPublicAddress: "owner-address",
            externalReferenceCode: null,
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreateSingleWinnerRule(),
            approvedClientApplications: Array.Empty<ApprovedClientApplicationRecord>(),
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition(
                    ElectionOptionDefinition.ReservedBlankOptionId,
                    ElectionOptionDefinition.ReservedBlankOptionLabel,
                    null,
                    0,
                    IsBlankOption: true),
            ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*reserved blank vote option*");
    }

    [Fact]
    public void CreateDraftRecord_WithDuplicateOptionOrder_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Board Election",
            shortDescription: null,
            ownerPublicAddress: "owner-address",
            externalReferenceCode: null,
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreateSingleWinnerRule(),
            approvedClientApplications: Array.Empty<ApprovedClientApplicationRecord>(),
            protocolOmegaVersion: "omega-v1.0.0",
            reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 1, IsBlankOption: false),
            ]);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*ballot order*");
    }

    [Fact]
    public void CreateDraftSnapshot_CapturesMetadataPolicyAndWarningState()
    {
        var election = ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Referendum",
            shortDescription: "Policy vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "REF-1",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.NonBinding,
            governanceMode: ElectionGovernanceMode.TrusteeThreshold,
            disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            outcomeRule: CreatePassFailRule(),
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
            acknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
                ElectionWarningCode.AllTrusteesRequiredFragility,
            ],
            requiredApprovalCount: 2);

        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            snapshotReason: "owner updated title",
            recordedByPublicAddress: "owner-address");

        snapshot.ElectionId.Should().Be(election.ElectionId);
        snapshot.DraftRevision.Should().Be(1);
        snapshot.Metadata.Title.Should().Be("Referendum");
        snapshot.Policy.GovernanceMode.Should().Be(ElectionGovernanceMode.TrusteeThreshold);
        snapshot.Policy.RequiredApprovalCount.Should().Be(2);
        snapshot.Options.Should().HaveCount(3);
        snapshot.Options.Last().OptionId.Should().Be(ElectionOptionDefinition.ReservedBlankOptionId);
        snapshot.AcknowledgedWarningCodes.Should().Equal(
            ElectionWarningCode.LowAnonymitySet,
            ElectionWarningCode.AllTrusteesRequiredFragility);
    }

    [Fact]
    public void CreateGovernedProposalApproval_BindsApprovalToExactProposalTarget()
    {
        var election = CreateTrusteeElection();
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Open,
            proposedByPublicAddress: "owner-address");

        var approval = ElectionModelFactory.CreateGovernedProposalApproval(
            proposal,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            approvalNote: "Looks correct.");

        approval.ProposalId.Should().Be(proposal.Id);
        approval.ElectionId.Should().Be(election.ElectionId);
        approval.ActionType.Should().Be(ElectionGovernedActionType.Open);
        approval.LifecycleStateAtProposalCreation.Should().Be(ElectionLifecycleState.Draft);
        approval.TrusteeUserAddress.Should().Be("trustee-a");
        approval.TrusteeDisplayName.Should().Be("Alice");
        approval.ApprovalNote.Should().Be("Looks correct.");
    }

    [Fact]
    public void GovernedProposalExecutionTransitions_PersistFailureAndRetryableSuccessMetadata()
    {
        var election = CreateTrusteeElection();
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election,
            ElectionGovernedActionType.Close,
            proposedByPublicAddress: "owner-address");
        var failed = proposal.RecordExecutionFailure(
            failureReason: "transition failed",
            attemptedAt: DateTime.UtcNow,
            executionTriggeredByPublicAddress: "owner-address");

        failed.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionFailed);
        failed.ExecutionFailureReason.Should().Be("transition failed");
        failed.CanRetry.Should().BeTrue();
        failed.IsPending.Should().BeTrue();

        var succeeded = failed.RecordExecutionSuccess(
            executedAt: DateTime.UtcNow,
            executionTriggeredByPublicAddress: "owner-address");

        succeeded.ExecutionStatus.Should().Be(ElectionGovernedProposalExecutionStatus.ExecutionSucceeded);
        succeeded.ExecutionFailureReason.Should().BeNull();
        succeeded.ExecutedAt.Should().NotBeNull();
        succeeded.CanRetry.Should().BeFalse();
        succeeded.IsPending.Should().BeFalse();
    }

    [Fact]
    public void CreateRosterEntry_SeparatesLinkVotingRightAndOpenSnapshotState()
    {
        var importedAt = DateTime.UtcNow.AddMinutes(-10);
        var linkedAt = importedAt.AddMinutes(1);
        var openedAt = importedAt.AddMinutes(2);
        var activatedAt = importedAt.AddMinutes(3);

        var entry = ElectionModelFactory.CreateRosterEntry(
            electionId: ElectionId.NewElectionId,
            organizationVoterId: "VOTER-1001",
            contactType: ElectionRosterContactType.Email,
            contactValue: "voter@example.com",
            votingRightStatus: ElectionVotingRightStatus.Inactive,
            importedAt: importedAt);

        entry.LinkStatus.Should().Be(ElectionVoterLinkStatus.Unlinked);
        entry.IsLinked.Should().BeFalse();
        entry.VotingRightStatus.Should().Be(ElectionVotingRightStatus.Inactive);
        entry.WasPresentAtOpen.Should().BeFalse();
        entry.WasActiveAtOpen.Should().BeFalse();

        var linkedEntry = entry.LinkToActor("voter-address", linkedAt);
        var frozenEntry = linkedEntry.FreezeAtOpen(openedAt);
        var activatedEntry = frozenEntry.MarkVotingRightActive("owner-address", activatedAt);

        linkedEntry.LinkedActorPublicAddress.Should().Be("voter-address");
        linkedEntry.LinkedAt.Should().Be(linkedAt);
        frozenEntry.WasPresentAtOpen.Should().BeTrue();
        frozenEntry.WasActiveAtOpen.Should().BeFalse();
        activatedEntry.VotingRightStatus.Should().Be(ElectionVotingRightStatus.Active);
        activatedEntry.WasActiveAtOpen.Should().BeFalse();
        activatedEntry.LastActivatedByPublicAddress.Should().Be("owner-address");
        activatedEntry.LastActivatedAt.Should().Be(activatedAt);
    }

    [Fact]
    public void CreateEligibilityActivationEvent_WithBlockedOutcomeAndNoReason_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateEligibilityActivationEvent(
            electionId: ElectionId.NewElectionId,
            organizationVoterId: "VOTER-1001",
            attemptedByPublicAddress: "owner-address",
            outcome: ElectionEligibilityActivationOutcome.Blocked);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*block reason*");
    }

    [Fact]
    public void CreateEligibilitySnapshot_WithInconsistentCounts_ShouldThrow()
    {
        var act = () => ElectionModelFactory.CreateEligibilitySnapshot(
            electionId: ElectionId.NewElectionId,
            snapshotType: ElectionEligibilitySnapshotType.Close,
            eligibilityMutationPolicy: EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
            rosteredCount: 3,
            linkedCount: 2,
            activeDenominatorCount: 2,
            countedParticipationCount: 2,
            blankCount: 1,
            didNotVoteCount: 1,
            rosteredVoterSetHash: [1, 2, 3],
            activeDenominatorSetHash: [4, 5, 6],
            countedParticipationSetHash: [7, 8, 9],
            recordedByPublicAddress: "owner-address");

        act.Should().Throw<ArgumentException>()
            .WithMessage("*did-not-vote*");
    }

    private static ElectionRecord CreateTrusteeElection() =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Governed Referendum",
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
            outcomeRule: CreatePassFailRule(),
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

    private static OutcomeRuleDefinition CreateSingleWinnerRule() =>
        new(
            OutcomeRuleKind.SingleWinner,
            "single_winner",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: false,
            TieResolutionRule: "tie_unresolved",
            CalculationBasis: "highest_non_blank_votes");

    private static OutcomeRuleDefinition CreatePassFailRule() =>
        new(
            OutcomeRuleKind.PassFail,
            "pass_fail_yes_no",
            SeatCount: 1,
            BlankVoteCountsForTurnout: true,
            BlankVoteExcludedFromWinnerSelection: true,
            BlankVoteExcludedFromThresholdDenominator: true,
            TieResolutionRule: "tie_unresolved",
            CalculationBasis: "simple_majority_of_non_blank_votes");
}
