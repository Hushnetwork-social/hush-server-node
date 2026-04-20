using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionReportPackageServiceTests
{
    [Fact]
    public void Build_WithAdminOnlyBindingElection_EmitsProtectedCustodyTruthInHumanArtifacts()
    {
        var service = new ElectionReportPackageService();
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Admin-only protected custody election",
            shortDescription: "FEAT-105 report package unit test",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-105-ADMIN",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.AdminOnly,
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
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var acceptedBallotSetHash = new byte[] { 1, 2, 3 };
        var publishedBallotStreamHash = new byte[] { 4, 5, 6 };
        var finalEncryptedTallyHash = new byte[] { 7, 8, 9 };
        var activeDenominatorSetHash = new byte[] { 10, 11, 12 };
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            draftElection,
            recordedByPublicAddress: "owner-address",
            frozenEligibleVoterSetHash: new byte[] { 13, 14, 15 },
            acceptedBallotCount: 1,
            acceptedBallotSetHash: acceptedBallotSetHash,
            publishedBallotCount: 1,
            publishedBallotStreamHash: publishedBallotStreamHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash);
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: acceptedBallotSetHash,
            publishedBallotCount: 1,
            publishedBallotStreamHash: publishedBallotStreamHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash);
        var finalizeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: acceptedBallotSetHash,
            publishedBallotCount: 1,
            publishedBallotStreamHash: publishedBallotStreamHash,
            finalEncryptedTallyHash: finalEncryptedTallyHash);

        var finalizedElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-2),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-1),
            FinalizedAt = DateTime.UtcNow,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            FinalizeArtifactId = finalizeArtifact.Id,
        };

        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            EligibilitySnapshotId: null,
            BoundaryArtifactId: closeArtifact.Id,
            ActiveDenominatorSetHash: activeDenominatorSetHash);
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Unofficial result",
            namedOptionResults:
            [
                new ElectionResultOptionCount("yes", "Yes", "Approve the proposal", 1, 1, 1),
                new ElectionResultOptionCount("no", "No", "Reject the proposal", 2, 2, 0),
            ],
            blankCount: 0,
            totalVotedCount: 1,
            eligibleToVoteCount: 1,
            didNotVoteCount: 0,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            publicPayload: "{\"mode\":\"binding\"}");
        var officialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Official result",
            namedOptionResults: unofficialResult.NamedOptionResults,
            blankCount: unofficialResult.BlankCount,
            totalVotedCount: unofficialResult.TotalVotedCount,
            eligibleToVoteCount: unofficialResult.EligibleToVoteCount,
            didNotVoteCount: unofficialResult.DidNotVoteCount,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            sourceResultArtifactId: unofficialResult.Id,
            publicPayload: "{\"mode\":\"binding\"}");

        finalizedElection = finalizedElection with
        {
            UnofficialResultArtifactId = unofficialResult.Id,
            OfficialResultArtifactId = officialResult.Id,
        };

        var buildResult = service.Build(new ElectionReportPackageBuildRequest(
            finalizedElection,
            closeArtifact,
            tallyReadyArtifact,
            finalizeArtifact,
            unofficialResult,
            officialResult,
            CloseEligibilitySnapshot: null,
            FinalizationSession: null,
            FinalizationReleaseEvidence: null,
            FinalizationGovernedProposal: null,
            FinalizationGovernedApprovals: Array.Empty<ElectionGovernedProposalApprovalRecord>(),
            FinalizationShares: Array.Empty<ElectionFinalizationShareRecord>(),
            WarningAcknowledgements: Array.Empty<ElectionWarningAcknowledgementRecord>(),
            TrusteeInvitations: Array.Empty<ElectionTrusteeInvitationRecord>(),
            RosterEntries:
            [
                ElectionModelFactory.CreateRosterEntry(
                    electionId,
                    "voter-alice",
                    ElectionRosterContactType.Email,
                    "alice@hush.test"),
            ],
            ParticipationRecords:
            [
                ElectionModelFactory.CreateParticipationRecord(
                    electionId,
                    "voter-alice",
                    ElectionParticipationStatus.CountedAsVoted),
            ],
            AttemptNumber: 1,
            PreviousAttemptId: null,
            AttemptedByPublicAddress: "owner-address",
            AttemptedAt: DateTime.UtcNow));

        buildResult.IsSuccess.Should().BeTrue();

        var humanManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        humanManifest.Content.Should().Contain("AdminOnly");
        humanManifest.Content.Should().Contain("admin-prod-1of1");
        humanManifest.Content.Should().Contain("production-like ceremony profiles");
        humanManifest.Content.Should().Contain("owner-admin protected custody profile");

        var humanAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport);
        humanAudit.Content.Should().Contain("AdminOnly");
        humanAudit.Content.Should().Contain("admin-prod-1of1");
        humanAudit.Content.Should().Contain("owner-admin protected custody profile");
        humanAudit.Content.Should().Contain("single-ballot inspection authority");

        var humanResult = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanResultReport);
        humanResult.Content.Should().Contain("production-like ceremony profiles");
        humanResult.Content.Should().Contain("admin-prod-1of1");
        humanResult.Content.Should().Contain("protected-ballot path");
        humanResult.Content.Should().Contain("Non-binding election: `no`");
    }

    [Fact]
    public void Build_WithAdminOnlyNonBindingProtectedElection_DoesNotMislabelItAsOpenAudit()
    {
        var service = new ElectionReportPackageService();
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Admin-only non-binding protected election",
            shortDescription: "FEAT-105 report truth regression",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-105-ADMIN-NONBINDING",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.NonBinding,
            selectedProfileId: "admin-prod-1of1",
            selectedProfileDevOnly: false,
            governanceMode: ElectionGovernanceMode.AdminOnly,
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
            reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("yes", "Yes", "Approve the proposal", 1, false),
                new ElectionOptionDefinition("no", "No", "Reject the proposal", 2, false),
            ],
            officialResultVisibilityPolicy: OfficialResultVisibilityPolicy.PublicPlaintext);

        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            draftElection,
            recordedByPublicAddress: "owner-address",
            frozenEligibleVoterSetHash: new byte[] { 1, 2, 3 },
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var finalizeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            draftElection,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });

        var finalizedElection = draftElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = DateTime.UtcNow.AddMinutes(-2),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-1),
            FinalizedAt = DateTime.UtcNow,
            CloseArtifactId = closeArtifact.Id,
            TallyReadyArtifactId = tallyReadyArtifact.Id,
            FinalizeArtifactId = finalizeArtifact.Id,
        };

        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            EligibilitySnapshotId: null,
            BoundaryArtifactId: closeArtifact.Id,
            ActiveDenominatorSetHash: new byte[] { 13, 14, 15 });
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Unofficial result",
            namedOptionResults:
            [
                new ElectionResultOptionCount("yes", "Yes", "Approve the proposal", 1, 1, 1),
                new ElectionResultOptionCount("no", "No", "Reject the proposal", 2, 2, 0),
            ],
            blankCount: 0,
            totalVotedCount: 1,
            eligibleToVoteCount: 1,
            didNotVoteCount: 0,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            publicPayload: "{\"mode\":\"protected-nonbinding\"}");
        var officialResult = ElectionModelFactory.CreateResultArtifact(
            electionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.PublicPlaintext,
            title: "Official result",
            namedOptionResults: unofficialResult.NamedOptionResults,
            blankCount: unofficialResult.BlankCount,
            totalVotedCount: unofficialResult.TotalVotedCount,
            eligibleToVoteCount: unofficialResult.EligibleToVoteCount,
            didNotVoteCount: unofficialResult.DidNotVoteCount,
            denominatorEvidence,
            recordedByPublicAddress: "owner-address",
            tallyReadyArtifactId: tallyReadyArtifact.Id,
            sourceResultArtifactId: unofficialResult.Id,
            publicPayload: "{\"mode\":\"protected-nonbinding\"}");

        finalizedElection = finalizedElection with
        {
            UnofficialResultArtifactId = unofficialResult.Id,
            OfficialResultArtifactId = officialResult.Id,
        };

        var buildResult = service.Build(new ElectionReportPackageBuildRequest(
            finalizedElection,
            closeArtifact,
            tallyReadyArtifact,
            finalizeArtifact,
            unofficialResult,
            officialResult,
            CloseEligibilitySnapshot: null,
            FinalizationSession: null,
            FinalizationReleaseEvidence: null,
            FinalizationGovernedProposal: null,
            FinalizationGovernedApprovals: Array.Empty<ElectionGovernedProposalApprovalRecord>(),
            FinalizationShares: Array.Empty<ElectionFinalizationShareRecord>(),
            WarningAcknowledgements: Array.Empty<ElectionWarningAcknowledgementRecord>(),
            TrusteeInvitations: Array.Empty<ElectionTrusteeInvitationRecord>(),
            RosterEntries:
            [
                ElectionModelFactory.CreateRosterEntry(
                    electionId,
                    "voter-alice",
                    ElectionRosterContactType.Email,
                    "alice@hush.test"),
            ],
            ParticipationRecords:
            [
                ElectionModelFactory.CreateParticipationRecord(
                    electionId,
                    "voter-alice",
                    ElectionParticipationStatus.CountedAsVoted),
            ],
            AttemptNumber: 1,
            PreviousAttemptId: null,
            AttemptedByPublicAddress: "owner-address",
            AttemptedAt: DateTime.UtcNow));

        buildResult.IsSuccess.Should().BeTrue();

        var humanResult = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanResultReport);
        humanResult.Content.Should().Contain("Binding status: `NonBinding`");
        humanResult.Content.Should().Contain("Non-binding election: `yes`");
        humanResult.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanResult.Content.Should().Contain("Circuit class: `Production`");
        humanResult.Content.Should().Contain("production-like ceremony profiles");
        humanResult.Content.Should().Contain("protected-ballot path");
        humanResult.Content.Should().NotContain("open-audit path");

        var humanManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        humanManifest.Content.Should().Contain("Binding status: `NonBinding`");
        humanManifest.Content.Should().Contain("Non-binding election: `yes`");
        humanManifest.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanManifest.Content.Should().Contain("Circuit class: `Production`");

        var humanAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport);
        humanAudit.Content.Should().Contain("Binding status: `NonBinding`");
        humanAudit.Content.Should().Contain("Non-binding election: `yes`");
        humanAudit.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanAudit.Content.Should().Contain("Circuit class: `Production`");

        var humanRoster = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanNamedParticipationRoster);
        humanRoster.Content.Should().Contain("Binding status: `NonBinding`");
        humanRoster.Content.Should().Contain("Non-binding election: `yes`");
        humanRoster.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanRoster.Content.Should().Contain("Circuit class: `Production`");

        var humanOutcome = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanOutcomeDetermination);
        humanOutcome.Content.Should().Contain("Binding status: `NonBinding`");
        humanOutcome.Content.Should().Contain("Non-binding election: `yes`");
        humanOutcome.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanOutcome.Content.Should().Contain("Circuit class: `Production`");

        var humanDispute = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanDisputeReviewIndex);
        humanDispute.Content.Should().Contain("Binding status: `NonBinding`");
        humanDispute.Content.Should().Contain("Non-binding election: `yes`");
        humanDispute.Content.Should().Contain("Selected circuit/profile: `admin-prod-1of1`");
        humanDispute.Content.Should().Contain("Circuit class: `Production`");

        var machineRoster = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineNamedParticipationRosterProjection);
        machineRoster.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineRoster.Content.Should().Contain("\"isNonBindingElection\": true");
        machineRoster.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineRoster.Content.Should().Contain("\"circuitClassification\": \"Production\"");

        var machineOutcome = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineOutcomeDeterminationProjection);
        machineOutcome.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineOutcome.Content.Should().Contain("\"isNonBindingElection\": true");
        machineOutcome.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineOutcome.Content.Should().Contain("\"circuitClassification\": \"Production\"");

        var machineDispute = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineDisputeReviewIndexProjection);
        machineDispute.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineDispute.Content.Should().Contain("\"isNonBindingElection\": true");
        machineDispute.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineDispute.Content.Should().Contain("\"circuitClassification\": \"Production\"");

        var machineEvidenceGraph = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        machineEvidenceGraph.Content.Should().Contain("\"bindingStatus\": \"NonBinding\"");
        machineEvidenceGraph.Content.Should().Contain("\"isNonBindingElection\": true");
        machineEvidenceGraph.Content.Should().Contain("\"selectedProfileId\": \"admin-prod-1of1\"");
        machineEvidenceGraph.Content.Should().Contain("\"circuitClassification\": \"Production\"");
    }

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
