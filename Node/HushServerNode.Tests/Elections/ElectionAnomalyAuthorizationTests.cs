using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyAuthorizationTests
{
    [Fact]
    public void ResolveActorSubmitter_WithMixedVoterTrusteeRole_DerivesOneAccountScope()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var rosterEntry = CreateLinkedRosterEntry(election.ElectionId, "actor-address", now);
        var trusteeInvitation = CreateAcceptedTrusteeInvitation(election.ElectionId, "actor-address", now);

        var voterResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            "actor-address",
            now,
            linkedRosterEntry: rosterEntry,
            acceptedTrusteeInvitation: trusteeInvitation,
            requestedRoleContextId: ElectionAnomalyActorRoleContextIds.Voter);
        var trusteeResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            "actor-address",
            now,
            linkedRosterEntry: rosterEntry,
            acceptedTrusteeInvitation: trusteeInvitation,
            requestedRoleContextId: ElectionAnomalyActorRoleContextIds.Trustee);

        voterResolution.IsResolved.Should().BeTrue();
        trusteeResolution.IsResolved.Should().BeTrue();
        trusteeResolution.SubmitterPersonScopeId.Should().Be(voterResolution.SubmitterPersonScopeId);
        voterResolution.RoleEvidenceTypeId.Should().Be(ElectionAnomalyRoleEvidenceTypeIds.VoterRosterLink);
        trusteeResolution.RoleEvidenceTypeId.Should().Be(ElectionAnomalyRoleEvidenceTypeIds.TrusteeInvitation);
        trusteeResolution.PersonScopeDerivationVersion.Should().Be(ElectionAnomalyPersonScopeDerivationVersions.Current);
    }

    [Fact]
    public void ResolveActorSubmitter_ShouldAcceptOwnerAndDesignatedAuditorEvidence()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var auditorGrant = new ElectionReportAccessGrantRecord(
            Guid.NewGuid(),
            election.ElectionId,
            "auditor-address",
            ElectionReportAccessGrantRole.DesignatedAuditor,
            now,
            election.OwnerPublicAddress);

        var ownerResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            election.OwnerPublicAddress,
            now);
        var auditorResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            "auditor-address",
            now,
            reportAccessGrant: auditorGrant);

        ownerResolution.IsResolved.Should().BeTrue();
        ownerResolution.RoleEvidenceTypeId.Should().Be(ElectionAnomalyRoleEvidenceTypeIds.ElectionOwner);
        ownerResolution.LifecycleStateAtSubmission.Should().Be(ElectionLifecycleState.Draft);
        auditorResolution.IsResolved.Should().BeTrue();
        auditorResolution.RoleEvidenceTypeId.Should().Be(ElectionAnomalyRoleEvidenceTypeIds.DesignatedAuditorGrant);
    }

    [Fact]
    public void ResolveExternalClaimantSubmitter_ShouldRequireOwnerAndUseClaimantReferenceScope()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);

        var ownerResolution = ElectionAnomalyAuthorization.ResolveExternalClaimantSubmitter(
            election,
            election.OwnerPublicAddress,
            "claimant-reference-hash",
            now);
        var forbiddenResolution = ElectionAnomalyAuthorization.ResolveExternalClaimantSubmitter(
            election,
            "auditor-address",
            "claimant-reference-hash",
            now);

        ownerResolution.IsResolved.Should().BeTrue();
        ownerResolution.RoleContextId.Should().Be(ElectionAnomalyActorRoleContextIds.ExternalClaimantRegistrar);
        ownerResolution.RoleEvidenceTypeId.Should().Be(ElectionAnomalyRoleEvidenceTypeIds.ExternalClaimantBridge);
        ownerResolution.SubmitterPersonScopeId.Should().StartWith("sha256:");
        forbiddenResolution.IsResolved.Should().BeFalse();
        forbiddenResolution.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.InvalidActionSignatory);
    }

    [Fact]
    public void ResolveActorSubmitter_WhenWindowClosed_ShouldRejectWithStableCode()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now) with
        {
            AnomalySubmissionWindowClosesAt = now.AddMinutes(-1),
        };

        var resolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            election.OwnerPublicAddress,
            now);

        resolution.IsResolved.Should().BeFalse();
        resolution.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.SubmissionWindowClosed);
    }

    [Fact]
    public void CanActorReadOwnThread_ShouldIgnoreCurrentRoleAndWindowState()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var submitResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            election.OwnerPublicAddress,
            now);
        var thread = new ElectionAnomalyThreadRecord(
            Guid.NewGuid(),
            election.ElectionId,
            submitResolution.SubmitterPersonScopeId!,
            submitResolution.PersonScopeDerivationVersion,
            submitResolution.ActorPublicAddress!,
            submitResolution.RoleContextId,
            submitResolution.RoleEvidenceTypeId!,
            submitResolution.RoleEvidenceReference!,
            submitResolution.LifecycleStateAtSubmission!.Value,
            now.AddMinutes(-1),
            ElectionAnomalyCategoryIds.AccessOrAuthenticationAnomaly,
            ElectionAnomalyCaseStateIds.Submitted,
            SeverityCandidateId: null,
            GovernedDecisionRef: null,
            HasOpenClarificationRequest: false,
            OpenClarificationRequestId: null,
            now,
            now,
            SourceTransactionId: Guid.NewGuid(),
            SourceBlockHeight: null,
            SourceBlockId: null,
            CurrentThreadHash: "sha256:thread");

        var ownRead = ElectionAnomalyAuthorization.CanActorReadOwnThread(thread, election.OwnerPublicAddress);
        var otherRead = ElectionAnomalyAuthorization.CanActorReadOwnThread(thread, "other-address");

        ownRead.CanRead.Should().BeTrue();
        otherRead.CanRead.Should().BeFalse();
        otherRead.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.ReadForbidden);
    }

    private static ElectionRecord CreateElection(DateTime now) =>
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
            ],
            createdAt: now);

    private static ElectionRosterEntryRecord CreateLinkedRosterEntry(
        ElectionId electionId,
        string actorPublicAddress,
        DateTime now) =>
        new ElectionRosterEntryRecord(
            ElectionId: electionId,
            OrganizationVoterId: "ORG-VOTER-1",
            ContactType: ElectionRosterContactType.Email,
            ContactValue: "voter@example.test",
            LinkStatus: ElectionVoterLinkStatus.Unlinked,
            LinkedActorPublicAddress: null,
            LinkedAt: null,
            VotingRightStatus: ElectionVotingRightStatus.Active,
            ImportedAt: now,
            WasPresentAtOpen: true,
            WasActiveAtOpen: true,
            LastActivatedAt: now,
            LastActivatedByPublicAddress: "owner-address",
            LastUpdatedAt: now,
            LatestTransactionId: null,
            LatestBlockHeight: null,
            LatestBlockId: null)
        .LinkToActor(actorPublicAddress, now);

    private static ElectionTrusteeInvitationRecord CreateAcceptedTrusteeInvitation(
        ElectionId electionId,
        string actorPublicAddress,
        DateTime now) =>
        new(
            Id: Guid.NewGuid(),
            ElectionId: electionId,
            TrusteeUserAddress: actorPublicAddress,
            TrusteeDisplayName: "Trustee",
            InvitedByPublicAddress: "owner-address",
            LinkedMessageId: null,
            Status: ElectionTrusteeInvitationStatus.Accepted,
            SentAtDraftRevision: 1,
            SentAt: now,
            ResolvedAtDraftRevision: 1,
            RespondedAt: now,
            RevokedAt: null,
            LatestTransactionId: null,
            LatestBlockHeight: null,
            LatestBlockId: null);
}
