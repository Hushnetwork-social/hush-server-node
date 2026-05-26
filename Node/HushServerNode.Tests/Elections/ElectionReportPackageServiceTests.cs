using System.IO.Compression;
using System.Text.Json;
using FluentAssertions;
using HushNode.Elections;
using HushShared.Elections.Model;
using HushShared.Elections.Verification.Model;
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
            ProtocolPackageBinding: null,
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
            ProtocolPackageBinding: null,
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

    [Fact]
    public void Build_WithSealedProtocolPackageBinding_EmitsRefsAndAccessLocationsInAuditArtifacts()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var binding = CreateSealedProtocolPackageBinding(election);
        var request = CreateReportBuildRequest(election, binding);

        var buildResult = service.Build(request);
        var buildWithoutBinding = service.Build(request with { ProtocolPackageBinding = null });

        buildResult.IsSuccess.Should().BeTrue();
        buildWithoutBinding.IsSuccess.Should().BeTrue();
        buildResult.Package.FrozenEvidenceHash.Should().NotEqual(buildWithoutBinding.Package.FrozenEvidenceHash);

        var machineManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineManifest);
        machineManifest.Content.Should().Contain("\"protocolPackageBinding\"");
        machineManifest.Content.Should().Contain("\"operationalSecurity\"");
        machineManifest.Content.Should().Contain("\"packageVersion\": \"v1.0.0\"");
        machineManifest.Content.Should().Contain($"\"specPackageHash\": \"{Hash('a')}\"");
        machineManifest.Content.Should().Contain($"\"proofPackageHash\": \"{Hash('b')}\"");
        machineManifest.Content.Should().Contain($"\"releaseManifestHash\": \"{Hash('c')}\"");
        machineManifest.Content.Should().Contain("\"externalReviewAvailability\": \"not_available\"");
        machineManifest.Content.Should().Contain("\"externalReviewClaimState\": \"program_defined\"");
        machineManifest.Content.Should().Contain(
            "\"externalReviewCustomerSafeSummary\": \"External examination program is defined; no reviewer conclusion is available.\"");
        machineManifest.Content.Should().Contain("https://www.hushnetwork.social/protocol-omega/hushvoting-v1/spec.zip");

        var machineEvidenceGraph = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        machineEvidenceGraph.Content.Should().Contain("\"protocolPackageBinding\"");
        machineEvidenceGraph.Content.Should().Contain("\"regulatoryClaim\"");
        machineEvidenceGraph.Content.Should().Contain($"\"releaseManifestHash\": \"{Hash('c')}\"");

        var machineAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineAuditProvenanceReportProjection);
        machineAudit.Content.Should().Contain("\"protocolPackageBinding\"");
        machineAudit.Content.Should().Contain("Temporary access-location outage is operational");

        var humanManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanManifest);
        humanManifest.Content.Should().Contain("Protocol package binding id");
        humanManifest.Content.Should().Contain($"Spec package hash: `{Hash('a')}`");
        humanManifest.Content.Should().Contain($"SP-08 release integrity manifest hash: `{Hash('c')}`");
        humanManifest.Content.Should().Contain("Spec access locations: `1`");
        humanManifest.Content.Should().Contain(
            "SP-09 external review summary: External examination program is defined; no reviewer conclusion is available.");
        humanManifest.Content.Should().Contain("SP-10 operational security boundary");
        humanManifest.Content.Should().Contain("SP-11 regulatory tracker claim: `not exported`");

        var humanAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport);
        humanAudit.Content.Should().Contain("## Protocol Omega Package Binding");
        humanAudit.Content.Should().Contain("## Operational Security And Regulatory Boundaries");
        humanAudit.Content.Should().Contain("Access-location note: Protocol package archives are referenced by immutable hashes");
        humanAudit.Content.Should().Contain("Website spec package");
        humanAudit.Content.Should().Contain("Website proof package");
        humanAudit.Content.Should().Contain("SP-11 legal validation boundary");
        humanAudit.Content.Should().NotContain("FEAT-106 complete");
        humanAudit.Content.Should().NotContain("Certified for public elections");
        ElectionSp09ProfileIds.ForbiddenClaimPhrases.Should().AllSatisfy(phrase =>
            humanAudit.Content.Contains(phrase, StringComparison.OrdinalIgnoreCase).Should().BeFalse());
    }

    [Fact]
    public void Build_WithRestrictedAnomalyIntakeManifest_AddsRestrictedArtifactAndEvidenceGraphNode()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var anomalyManifest = CreateRestrictedAnomalyIntakeManifest(election.ElectionId);
        var request = CreateReportBuildRequest(election, protocolPackageBinding: null) with
        {
            RestrictedAnomalyIntakeManifest = anomalyManifest,
        };

        var buildResult = service.Build(request);

        buildResult.IsSuccess.Should().BeTrue();
        buildResult.Package.ArtifactCount.Should().Be(14);

        var anomalyArtifact = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineRestrictedAnomalyIntakeManifest);
        anomalyArtifact.AccessScope.Should().Be(ElectionReportArtifactAccessScope.OwnerAuditorOnly);
        anomalyArtifact.FileName.Should().Be("restricted-anomaly-intake-manifest.json");
        anomalyArtifact.Content.Should().Contain("\"artifactSchemaId\": \"restricted-anomaly-intake-manifest-artifact-v1\"");
        anomalyArtifact.Content.Should().Contain("\"manifestHash\": \"sha256:");
        anomalyArtifact.Content.Should().Contain("\"scopeId\": \"package\"");
        anomalyArtifact.Content.Should().Contain("\"packageReadinessStatusId\": \"blocked\"");
        anomalyArtifact.Content.Should().Contain("\"scannerStatusId\": \"pending\"");

        var evidenceGraph = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        evidenceGraph.Content.Should().Contain("\"restrictedAnomalyIntakeManifest\"");
        evidenceGraph.Content.Should().Contain("\"nodeType\": \"anomaly_intake_manifest\"");
        evidenceGraph.Content.Should().Contain($"\"artifactId\": \"{anomalyArtifact.Id}\"");
        evidenceGraph.Content.Should().Contain("\"scopeId\": \"package\"");
        evidenceGraph.Content.Should().Contain("\"threadCount\": 1");
        evidenceGraph.Content.Should().Contain("\"attachmentManifestCount\": 1");
        evidenceGraph.Content.Should().Contain("\"redactionCount\": 1");
        evidenceGraph.Content.Should().Contain("\"anomalyThreadIds\"");
        evidenceGraph.Content.Should().Contain($"\"{anomalyManifest.Threads[0].AnomalyThreadId}\"");
        evidenceGraph.Content.Should().Contain("\"attachmentManifestIds\"");
        evidenceGraph.Content.Should().Contain($"\"{anomalyManifest.Threads[0].Attachments[0].AttachmentManifestId}\"");
        evidenceGraph.Content.Should().Contain("\"redactionEventIds\"");
        evidenceGraph.Content.Should().Contain($"\"{anomalyManifest.Threads[0].Redactions[0].RedactionEventId}\"");
        evidenceGraph.Content.Should().Contain("\"sourceEventIds\"");
        evidenceGraph.Content.Should().Contain($"\"{anomalyManifest.Threads[0].Attachments[0].EventId}\"");
        evidenceGraph.Content.Should().Contain($"\"{anomalyManifest.Threads[0].Redactions[0].EventId}\"");

        var disputeIndex = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineDisputeReviewIndexProjection);
        disputeIndex.Content.Should().Contain("MachineRestrictedAnomalyIntakeManifest");
    }

    [Fact]
    [Trait("Category", "FEAT-143")]
    public void Build_WithDeploymentProofBindingLedger_AddsPublicLedgerArtifactAndReportRefs()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var request = CreateReportBuildRequest(election, protocolPackageBinding: null) with
        {
            DeploymentProofBindingLedger = CreateDeploymentProofPublicLedger(election.ElectionId),
        };

        var buildResult = service.Build(request);

        buildResult.IsSuccess.Should().BeTrue();
        buildResult.Package.ArtifactCount.Should().Be(14);

        var ledgerArtifact = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineDeploymentProofBindingLedger);
        ledgerArtifact.AccessScope.Should().Be(ElectionReportArtifactAccessScope.Public);
        ledgerArtifact.FileName.Should().Be(ElectionDeploymentProofConstants.PublicLedgerArtifactFileName);
        ledgerArtifact.Content.Should().Contain("\"schemaId\": \"hushvoting-deployment-proof-public-ledger-v1\"");
        ledgerArtifact.Content.Should().Contain("\"finalStatus\": \"AcceptedWithLimitations\"");
        ledgerArtifact.Content.Should().Contain("\"claimEffect\": \"AcceptedWithLimitations\"");
        ledgerArtifact.Content.Should().Contain("\"claimLimitations\"");
        ledgerArtifact.Content.Should().Contain(ElectionDeploymentProofConstants.Feat144WebClientProofNotSupportedCode);
        ledgerArtifact.Content.Should().NotContain("private key");
        ledgerArtifact.Content.Should().NotContain("raw log");
        ledgerArtifact.Content.Should().NotContain("support log");
        ledgerArtifact.Content.Should().NotContain("voter identity");

        var machineManifest = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineManifest);
        machineManifest.Content.Should().Contain("\"deploymentProofBinding\"");
        machineManifest.Content.Should().Contain($"\"ledgerArtifactId\": \"{ledgerArtifact.Id}\"");
        machineManifest.Content.Should().Contain($"\"ledgerArtifactHash\": \"{HashBytesAsHex(ledgerArtifact.ContentHash)}\"");

        var evidenceGraph = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        evidenceGraph.Content.Should().Contain("\"deploymentProofBinding\"");
        evidenceGraph.Content.Should().Contain("\"blocksDeploymentProofClaims\": false");

        var humanAudit = buildResult.Artifacts.Single(x => x.ArtifactKind == ElectionReportArtifactKind.HumanAuditProvenanceReport);
        humanAudit.Content.Should().Contain("## Deployment Proof Binding");
        humanAudit.Content.Should().Contain("Deployment proof status: `AcceptedWithLimitations`");
        humanAudit.Content.Should().Contain("WebClient proof status: `NotYetSupported`");
        humanAudit.Content.Should().Contain("FEAT-137 retention/log privacy claim effect: `Accepted`");
        humanAudit.Content.Should().Contain("complete WebClient proof binding remains downgraded");
        humanAudit.Content.Should().Contain("Deployment proof status is separate from election outcome authority");

        var disputeIndex = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineDisputeReviewIndexProjection);
        disputeIndex.Content.Should().Contain("MachineDeploymentProofBindingLedger");
    }

    [Fact]
    [Trait("Category", "FEAT-139")]
    public void Build_WithAbnormalFinalizationEvidence_AddsVerifierReadyEvidenceArtifact()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var request = CreateReportBuildRequest(election, protocolPackageBinding: null) with
        {
            AbnormalFinalizationEvidence = new AbnormalFinalizationReportPackageEvidenceInput(
                AuthorityDecisionRef: "governance-decision:abnormal-finalization-0001",
                AuthorityDecisionHash: Hash('d'),
                GovernanceRuleRef: "customer-rule:finality-policy-v1",
                MissingFinalizeEvidence:
                [
                    "trustee-approval:missing:keylost-trustee",
                ],
                ContinuityIncidentEvidenceRefs:
                [
                    "continuity-incident:keylost-trustee",
                ],
                AvailableTrusteeAcknowledgementRefs:
                [
                    "trustee-ack:available-trustee-1",
                ],
                PublicSummary: "The fixed unofficial result was accepted by the configured authority after normal finalization could not complete cleanly.",
                DecidedAtUtc: new DateTime(2026, 5, 4, 12, 12, 0, DateTimeKind.Utc)),
        };

        var buildResult = service.Build(request);

        buildResult.IsSuccess.Should().BeTrue();
        var artifact = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineAbnormalFinalizationEvidence);
        artifact.AccessScope.Should().Be(ElectionReportArtifactAccessScope.OwnerAuditorTrustee);
        artifact.FileName.Should().Be("abnormal-finalization-evidence.json");

        var evidence = JsonSerializer.Deserialize<AbnormalFinalizationEvidenceArtifactRecord>(
            artifact.Content,
            VerificationJson.Options)!;
        evidence.ArtifactSchemaId.Should().Be(AbnormalFinalizationVerificationIds.ArtifactSchemaId);
        evidence.ReportPackageId.Should().Be(buildResult.Package.Id.ToString());
        evidence.OutcomeStatus.Should().Be(AbnormalFinalizationVerificationIds.OutcomeStatusFinalizedWithAnomaly);
        evidence.CleanFinalization.Should().BeFalse();
        evidence.OfficialResultSource.Should().Be(
            AbnormalFinalizationVerificationIds.OfficialResultSourceCopiedFromFixedUnofficial);
        evidence.OfficialResultSourceArtifactId.Should().Be(request.UnofficialResult.Id.ToString());
        evidence.MissingFinalizeEvidence.Should().Contain("trustee-approval:missing:keylost-trustee");
    }

    [Theory]
    [InlineData(ElectionAnomalyPackageReadinessStatusIds.Ready)]
    [InlineData(ElectionAnomalyPackageReadinessStatusIds.Warning)]
    public void Build_WithRestrictedAnomalyReadinessState_ExportsStatusToArtifactAndGraph(
        string packageReadinessStatusId)
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var blockers = packageReadinessStatusId == ElectionAnomalyPackageReadinessStatusIds.Ready
            ? Array.Empty<string>()
            : [ElectionAnomalyPayloadAvailabilityStatusIds.ManifestHashMismatch];
        var anomalyManifest = CreateRestrictedAnomalyIntakeManifest(election.ElectionId) with
        {
            PackageReadinessStatusId = packageReadinessStatusId,
            PackageReadinessBlockerIds = blockers,
        };
        var request = CreateReportBuildRequest(election, protocolPackageBinding: null) with
        {
            RestrictedAnomalyIntakeManifest = anomalyManifest,
        };

        var buildResult = service.Build(request);

        buildResult.IsSuccess.Should().BeTrue();
        var anomalyArtifact = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineRestrictedAnomalyIntakeManifest);
        var evidenceGraph = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineEvidenceGraph);
        anomalyArtifact.Content.Should().Contain($"\"packageReadinessStatusId\": \"{packageReadinessStatusId}\"");
        evidenceGraph.Content.Should().Contain($"\"packageReadinessStatusId\": \"{packageReadinessStatusId}\"");
        foreach (var blocker in blockers)
        {
            anomalyArtifact.Content.Should().Contain($"\"{blocker}\"");
            evidenceGraph.Content.Should().Contain($"\"{blocker}\"");
        }
    }

    [Fact]
    public void Build_WithRestrictedAnomalyIntakeManifest_AddsPublicSummaryToResultReports()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var anomalyManifest = CreateRestrictedAnomalyIntakeManifest(election.ElectionId);
        var request = CreateReportBuildRequest(election, protocolPackageBinding: null) with
        {
            RestrictedAnomalyIntakeManifest = anomalyManifest,
        };

        var buildResult = service.Build(request);

        buildResult.IsSuccess.Should().BeTrue();
        var resultProjection = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.MachineResultReportProjection);
        resultProjection.Content.Should().Contain("\"publicAnomalySummary\"");
        resultProjection.Content.Should().Contain("\"schemaId\": \"public-anomaly-summary-v1\"");
        resultProjection.Content.Should().Contain("\"suppressionPolicyId\": \"anomaly-public-summary-v1\"");
        resultProjection.Content.Should().Contain("\"sourceManifestHash\": \"sha256:");
        resultProjection.Content.Should().Contain("\"totalThreadCount\": null");
        resultProjection.Content.Should().Contain("\"totalThreadCountMode\": \"suppressed\"");
        resultProjection.Content.Should().Contain("\"low_count_category\"");
        resultProjection.Content.Should().Contain("\"small_election_identifiability\"");
        resultProjection.Content.Should().Contain("\"restrictedManifestArtifactId\"");
        resultProjection.Content.Should().Contain("\"restrictedManifestHash\": \"sha256:");
        resultProjection.Content.Should().Contain("\"anomalyReportReadiness\"");
        resultProjection.Content.Should().Contain("\"forbiddenFieldScanStatusId\": \"passed\"");
        resultProjection.Content.Should().Contain("\"retentionEvidenceStatusId\": \"open_case_requires_policy_review\"");
        resultProjection.Content.Should().Contain("\"reportGenerationReadOnlyStatusId\": \"validated\"");

        var humanReport = buildResult.Artifacts.Single(x =>
            x.ArtifactKind == ElectionReportArtifactKind.HumanResultReport);
        humanReport.Content.Should().Contain("## Anomaly Reporting");
        humanReport.Content.Should().Contain("Public anomaly thread count: suppressed by privacy policy.");
        humanReport.Content.Should().Contain("Restricted anomaly evidence is available only in the owner/auditor report package artifact.");
        humanReport.Content.Should().Contain("`low_count_category`");
        humanReport.Content.Should().Contain("`small_election_identifiability`");
        humanReport.Content.Should().Contain("Retention evidence status: `open_case_requires_policy_review`");
        humanReport.Content.Should().Contain("Report generation read-only status: `validated`");
    }

    [Fact]
    public void Build_PublicAnomalyResultReports_DoNotContainRestrictedManifestFieldsOrValues()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var anomalyManifest = CreateRestrictedAnomalyIntakeManifest(election.ElectionId);
        var restrictedPayloadReference = anomalyManifest.Threads[0].Attachments[0].EncryptedPayloadReference;
        var restrictedEventHash = anomalyManifest.Threads[0].Attachments[0].EventHash;
        var request = CreateReportBuildRequest(election, protocolPackageBinding: null) with
        {
            RestrictedAnomalyIntakeManifest = anomalyManifest,
        };

        var buildResult = service.Build(request);

        buildResult.IsSuccess.Should().BeTrue();
        var publicArtifacts = buildResult.Artifacts
            .Where(x => x.ArtifactKind is
                ElectionReportArtifactKind.MachineResultReportProjection or
                ElectionReportArtifactKind.HumanResultReport)
            .ToArray();
        publicArtifacts.Should().HaveCount(2);
        foreach (var artifact in publicArtifacts)
        {
            var scan = ElectionAnomalyPublicArtifactPrivacyScanner.Scan(
                artifact.Content,
                [restrictedPayloadReference, restrictedEventHash]);
            scan.Passed.Should().BeTrue($"public artifact {artifact.FileName} must not expose restricted anomaly fields or values");
        }
    }

    [Fact]
    public void Build_WithAnomalyManifest_DoesNotMutateReportInputs()
    {
        var service = new ElectionReportPackageService();
        var election = CreateFinalizedElectionForReportPackage();
        var anomalyManifest = CreateRestrictedAnomalyIntakeManifest(election.ElectionId);
        var request = CreateReportBuildRequest(election, protocolPackageBinding: null) with
        {
            RestrictedAnomalyIntakeManifest = anomalyManifest,
        };
        var electionBefore = JsonSerializer.Serialize(request.Election, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var manifestBefore = JsonSerializer.Serialize(anomalyManifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var buildResult = service.Build(request);

        buildResult.IsSuccess.Should().BeTrue();
        JsonSerializer.Serialize(request.Election, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().Be(electionBefore);
        JsonSerializer.Serialize(anomalyManifest, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .Should().Be(manifestBefore);
    }

    [Fact]
    [Trait("Category", "FEAT-138")]
    public void BuildVoid_WithVoidedElection_CreatesPublicVoidPackageAndRestrictedEvidenceIndex()
    {
        var service = new ElectionReportPackageService();
        var internalEvidenceRecordId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var request = CreateVoidReportBuildRequest(internalEvidenceRecordId);

        var buildResult = service.BuildVoid(request);

        buildResult.IsSuccess.Should().BeTrue();
        buildResult.Package.PackageKind.Should().Be(ElectionReportPackageKind.Void);
        buildResult.Package.VoidDecisionId.Should().Be(request.Decision.Id);
        buildResult.Package.VoidPublicationAttemptId.Should().Be(request.PublicationAttempt.Id);
        buildResult.PublicationAttempt.Status.Should().Be(ElectionVoidPublicationAttemptStatus.Sealed);
        buildResult.PublicationAttempt.ReportPackageId.Should().Be(buildResult.Package.Id);
        buildResult.PublicStatus.Should().NotBeNull();
        buildResult.PublicStatus!.Status.Should().Be("VOID");
        buildResult.PublicStatus.VerifierResultCode.Should().Be(VerificationResultCodes.ElectionVoided);
        buildResult.RestrictedEvidenceIndex.Should().NotBeNull();

        buildResult.Artifacts.Select(x => x.FileName).Should().Contain(
        [
            VerificationPackageFileNames.VoidDecision,
            VerificationPackageFileNames.PublicVoidSummary,
            VerificationPackageFileNames.VoidPublicStatus,
            VerificationPackageFileNames.VoidSupersededArtifacts,
            VerificationPackageFileNames.VoidVerifierResult,
            VerificationPackageFileNames.RestrictedVoidEvidenceIndex,
            VerificationPackageFileNames.RestrictedHistoricalUnofficialResult,
            VerificationPackageFileNames.VoidPackageManifest,
            VerificationPackageFileNames.VoidPackageArchive,
        ]);

        var publicArtifacts = buildResult.Artifacts
            .Where(x => x.AccessScope == ElectionReportArtifactAccessScope.Public)
            .ToArray();
        publicArtifacts.Select(x => x.FileName).Should().Contain(
        [
            VerificationPackageFileNames.VoidDecision,
            VerificationPackageFileNames.PublicVoidSummary,
            VerificationPackageFileNames.VoidPublicStatus,
            VerificationPackageFileNames.VoidSupersededArtifacts,
            VerificationPackageFileNames.VoidVerifierResult,
            VerificationPackageFileNames.VoidPackageManifest,
            VerificationPackageFileNames.VoidPackageArchive,
        ]);
        publicArtifacts.Select(x => x.FileName).Should().NotContain(VerificationPackageFileNames.RestrictedVoidEvidenceIndex);
        publicArtifacts.Select(x => x.FileName).Should().NotContain(VerificationPackageFileNames.RestrictedHistoricalUnofficialResult);

        var publicText = string.Join(
            "\n",
            publicArtifacts
                .Where(x => x.Format != ElectionReportArtifactFormat.Binary)
                .Select(x => x.Content));
        publicText.Should().NotContain("internalRecordId");
        publicText.Should().NotContain("Internal record id");
        publicText.Should().NotContain(internalEvidenceRecordId.ToString());
        publicText.Should().Contain("\"resultCode\": \"election_voided\"");

        var restrictedIndex = buildResult.Artifacts.Single(x =>
            x.FileName == VerificationPackageFileNames.RestrictedVoidEvidenceIndex);
        restrictedIndex.AccessScope.Should().Be(ElectionReportArtifactAccessScope.OwnerAuditorOnly);
        restrictedIndex.Content.Should().Contain(internalEvidenceRecordId.ToString());

        var manifestArtifact = buildResult.Artifacts.Single(x =>
            x.FileName == VerificationPackageFileNames.VoidPackageManifest);
        manifestArtifact.Content.Should().NotContain(VerificationPackageFileNames.RestrictedVoidEvidenceIndex);
        manifestArtifact.Content.Should().NotContain(VerificationPackageFileNames.RestrictedHistoricalUnofficialResult);

        var zipArtifact = buildResult.Artifacts.Single(x =>
            x.FileName == VerificationPackageFileNames.VoidPackageArchive);
        zipArtifact.Format.Should().Be(ElectionReportArtifactFormat.Binary);
        zipArtifact.MediaType.Should().Be("application/zip");
        using var archive = new ZipArchive(
            new MemoryStream(Convert.FromBase64String(zipArtifact.Content)),
            ZipArchiveMode.Read);
        archive.Entries.Select(x => x.FullName).Should().Contain(
        [
            VerificationPackageFileNames.VoidDecision,
            VerificationPackageFileNames.PublicVoidSummary,
            VerificationPackageFileNames.VoidPublicStatus,
            VerificationPackageFileNames.VoidSupersededArtifacts,
            VerificationPackageFileNames.VoidVerifierResult,
            VerificationPackageFileNames.VoidPackageManifest,
        ]);
        archive.Entries.Select(x => x.FullName).Should().NotContain(VerificationPackageFileNames.RestrictedVoidEvidenceIndex);
        archive.Entries.Select(x => x.FullName).Should().NotContain(VerificationPackageFileNames.RestrictedHistoricalUnofficialResult);
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

    private static ElectionRecord CreateFinalizedElectionForReportPackage()
    {
        var electionId = ElectionId.NewElectionId;
        var draftElection = ElectionModelFactory.CreateDraftRecord(
            electionId,
            title: "Protocol package report election",
            shortDescription: "FEAT-112 report package refs",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "FEAT-112-REPORT",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            selectedProfileId: ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
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

        return draftElection with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc),
            TallyReadyAt = new DateTime(2026, 5, 4, 12, 5, 0, DateTimeKind.Utc),
            FinalizedAt = new DateTime(2026, 5, 4, 12, 10, 0, DateTimeKind.Utc),
        };
    }

    private static ElectionReportPackageBuildRequest CreateReportBuildRequest(
        ElectionRecord election,
        ProtocolPackageBindingRecord? protocolPackageBinding)
    {
        var closeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Close,
            election,
            recordedByPublicAddress: "owner-address",
            frozenEligibleVoterSetHash: new byte[] { 1, 2, 3 },
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var tallyReadyArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.TallyReady,
            election,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var finalizeArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Finalize,
            election,
            recordedByPublicAddress: "owner-address",
            acceptedBallotCount: 1,
            acceptedBallotSetHash: new byte[] { 4, 5, 6 },
            publishedBallotCount: 1,
            publishedBallotStreamHash: new byte[] { 7, 8, 9 },
            finalEncryptedTallyHash: new byte[] { 10, 11, 12 });
        var finalizedElection = election with
        {
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
            election.ElectionId,
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
            publicPayload: "{\"mode\":\"protocol-package-report\"}");
        var officialResult = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
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
            publicPayload: "{\"mode\":\"protocol-package-report\"}");

        return new ElectionReportPackageBuildRequest(
            finalizedElection with
            {
                UnofficialResultArtifactId = unofficialResult.Id,
                OfficialResultArtifactId = officialResult.Id,
            },
            closeArtifact,
            tallyReadyArtifact,
            finalizeArtifact,
            unofficialResult,
            officialResult,
            CloseEligibilitySnapshot: null,
            ProtocolPackageBinding: protocolPackageBinding,
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
                    election.ElectionId,
                    "voter-alice",
                    ElectionRosterContactType.Email,
                    "alice@hush.test"),
            ],
            ParticipationRecords:
            [
                ElectionModelFactory.CreateParticipationRecord(
                    election.ElectionId,
                    "voter-alice",
                    ElectionParticipationStatus.CountedAsVoted),
            ],
            AttemptNumber: 1,
            PreviousAttemptId: null,
            AttemptedByPublicAddress: "owner-address",
            AttemptedAt: new DateTime(2026, 5, 4, 12, 11, 0, DateTimeKind.Utc));
    }

    private static ElectionVoidReportPackageBuildRequest CreateVoidReportBuildRequest(Guid internalEvidenceRecordId)
    {
        var preVoidElection = CreateFinalizedElectionForReportPackage() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            FinalizedAt = null,
            OfficialResultArtifactId = null,
            CloseArtifactId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000001"),
            TallyReadyArtifactId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000002"),
            ClosedAt = new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc),
            TallyReadyAt = new DateTime(2026, 5, 4, 12, 5, 0, DateTimeKind.Utc),
        };
        var denominatorEvidence = new ElectionResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            EligibilitySnapshotId: null,
            BoundaryArtifactId: preVoidElection.CloseArtifactId,
            ActiveDenominatorSetHash: new byte[] { 1, 2, 3, 4 });
        var unofficialResult = ElectionModelFactory.CreateResultArtifact(
            preVoidElection.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.PublicPlaintext,
            "Historical unofficial result",
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
            tallyReadyArtifactId: preVoidElection.TallyReadyArtifactId,
            publicPayload: "{\"mode\":\"historical-unofficial\"}",
            recordedAt: new DateTime(2026, 5, 4, 12, 6, 0, DateTimeKind.Utc));
        preVoidElection = preVoidElection with
        {
            UnofficialResultArtifactId = unofficialResult.Id,
        };
        var evidenceReference = ElectionModelFactory.CreateVoidEvidenceReference(
            ElectionVoidEvidenceReferenceKind.InternalAnomalyThread,
            "ANOMALY-THREAD-0001",
            internalEvidenceRecordId,
            referenceHash: "sha256:1111111111111111111111111111111111111111111111111111111111111111",
            recordedAt: new DateTime(2026, 5, 4, 12, 7, 0, DateTimeKind.Utc));
        var decision = ElectionModelFactory.CreateVoidDecision(
            preVoidElection,
            "owner-address",
            "The election owner accepted a governance dispute and voided the election.",
            Guid.Parse("bbbbbbbb-0000-0000-0000-000000000003"),
            [evidenceReference],
            sourceTransactionId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000004"),
            sourceBlockHeight: 88,
            sourceBlockId: Guid.Parse("bbbbbbbb-0000-0000-0000-000000000005"),
            decidedAt: new DateTime(2026, 5, 4, 12, 8, 0, DateTimeKind.Utc));
        var pendingAttempt = ElectionModelFactory.CreatePendingVoidPublicationAttempt(
            preVoidElection.ElectionId,
            decision.Id,
            attemptNumber: 1,
            frozenEvidenceHash: new byte[] { 9, 8, 7, 6 },
            frozenEvidenceFingerprint: "sha256:09080706",
            attemptedByPublicAddress: "owner-address",
            attemptedAt: new DateTime(2026, 5, 4, 12, 9, 0, DateTimeKind.Utc));
        decision = decision with
        {
            CurrentPublicationAttemptId = pendingAttempt.Id,
        };
        var voidedElection = preVoidElection with
        {
            LifecycleState = ElectionLifecycleState.Voided,
            VoteAcceptanceLockedAt = new DateTime(2026, 5, 4, 12, 8, 0, DateTimeKind.Utc),
        };
        var supersededPackageId = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000006");
        var supersededArtifacts =
            new[]
            {
                ElectionModelFactory.CreateVoidSupersededArtifact(
                    preVoidElection.ElectionId,
                    decision.Id,
                    ElectionVoidSupersededArtifactKind.ReportPackage,
                    $"report-package:{supersededPackageId:N}",
                    reportPackageId: supersededPackageId,
                    artifactHash: "sha256:2222222222222222222222222222222222222222222222222222222222222222",
                    supersededAt: new DateTime(2026, 5, 4, 12, 8, 30, DateTimeKind.Utc)),
            };

        return new ElectionVoidReportPackageBuildRequest(
            voidedElection,
            decision,
            pendingAttempt,
            supersededArtifacts,
            unofficialResult,
            AttemptNumber: 1,
            PreviousReportPackageId: supersededPackageId,
            AttemptedByPublicAddress: "owner-address",
            AttemptedAt: new DateTime(2026, 5, 4, 12, 9, 0, DateTimeKind.Utc));
    }

    private static ProtocolPackageBindingRecord CreateSealedProtocolPackageBinding(ElectionRecord election)
    {
        var catalogEntry = ElectionModelFactory.CreateApprovedProtocolPackageCatalogEntry(
            packageId: "omega-hushvoting-v1",
            packageVersion: "v1.0.0",
            specPackageHash: Hash('a'),
            proofPackageHash: Hash('b'),
            releaseManifestHash: Hash('c'),
            compatibleProfileIds:
            [
                election.SelectedProfileId,
            ],
            approvalStatus: ProtocolPackageApprovalStatus.ApprovedInternal,
            isLatestForCompatibleProfiles: true,
            specAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.PublicWebsite,
                    "Website spec package",
                    "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/spec.zip",
                    Hash('d')),
            ],
            proofAccessLocations:
            [
                ElectionModelFactory.CreateProtocolPackageAccessLocation(
                    ProtocolPackageAccessLocationKind.PublicWebsite,
                    "Website proof package",
                    "https://www.hushnetwork.social/protocol-omega/hushvoting-v1/proof.zip",
                    Hash('e')),
            ],
            approvedAt: new DateTime(2026, 5, 4, 11, 30, 0, DateTimeKind.Utc));

        var binding = ElectionModelFactory.CreateProtocolPackageBindingFromCatalog(
            election.ElectionId,
            catalogEntry,
            election.SelectedProfileId,
            election.CurrentDraftRevision,
            election.OwnerPublicAddress,
            boundAt: new DateTime(2026, 5, 4, 11, 45, 0, DateTimeKind.Utc));

        return binding.SealAtOpen(
            new DateTime(2026, 5, 4, 12, 0, 0, DateTimeKind.Utc),
            election.OwnerPublicAddress,
            sourceTransactionId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
            sourceBlockHeight: 42,
            sourceBlockId: Guid.Parse("22222222-2222-2222-2222-222222222222"));
    }

    private static AnomalyIntakeManifest CreateRestrictedAnomalyIntakeManifest(ElectionId electionId)
    {
        var threadId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
        var attachmentManifestId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var recordedAt = new DateTime(2026, 5, 4, 12, 8, 0, DateTimeKind.Utc);

        return new AnomalyIntakeManifest(
            ElectionAnomalyManifestCanonicalizationIds.Current,
            electionId.ToString(),
            ElectionAnomalyEvidenceManifestScopeIds.Package,
            ElectionAnomalyPackageReadinessStatusIds.Blocked,
            [ElectionAnomalyEvidenceScannerStatusIds.Pending],
            [
                new AnomalyIntakeManifestThread(
                    threadId,
                    ElectionAnomalyCategoryIds.SecurityOrIntegrityConcern,
                    ElectionAnomalyCaseStateIds.UnderReview,
                    "sha256:thread",
                    GovernedDecisionRef: "proposal-1",
                    HasOpenClarificationRequest: true,
                    OpenClarificationRequestId: Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc"),
                    recordedAt.AddMinutes(-20),
                    recordedAt,
                    Attachments:
                    [
                        new AnomalyIntakeManifestAttachment(
                            attachmentManifestId,
                            Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd"),
                            $"sha256:{Hash('e')}",
                            ElectionAnomalyAttachmentKindIds.SubmitterEvidence,
                            $"{ElectionAnomalyRestrictedPayloadReference.Prefix}eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee",
                            $"sha256:{Hash('a')}",
                            $"sha256:{Hash('b')}",
                            2048,
                            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
                            ElectionAnomalyAttachmentValidationStatusIds.PendingScan,
                            ElectionAnomalyEvidenceScannerStatusIds.Pending,
                            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
                            ClarificationRequestId: null,
                            recordedAt,
                            Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff")),
                    ],
                    Redactions:
                    [
                        new AnomalyIntakeManifestRedaction(
                            Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            $"sha256:{Hash('f')}",
                            ElectionAnomalyRedactionTargetKindIds.AttachmentManifest,
                            attachmentManifestId.ToString(),
                            ElectionAnomalyRedactionReasonIds.PersonalData,
                            $"sha256:{Hash('b')}",
                            ReplacementManifestHash: null,
                            TombstoneStatusId: "redacted",
                            recordedAt.AddMinutes(1),
                            Guid.Parse("33333333-3333-3333-3333-333333333333")),
                    ],
                    RecipientStatuses:
                    [
                        new AnomalyIntakeManifestRecipientStatus(
                            ElectionAnomalyRecipientRoleIds.ElectionOwner,
                            ElectionAnomalyRecipientWrapStatusIds.Available),
                    ]),
            ]);
    }

    private static ElectionDeploymentProofPublicLedgerArtifactRecord CreateDeploymentProofPublicLedger(
        ElectionId electionId)
    {
        var ledgerId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
        var checkpointId = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
        var observedAt = new DateTime(2026, 5, 4, 12, 10, 30, DateTimeKind.Utc);

        return new ElectionDeploymentProofPublicLedgerArtifactRecord(
            ElectionDeploymentProofConstants.PublicLedgerArtifactSchemaId,
            electionId.ToString(),
            ledgerId,
            $"deployment-ledger-{electionId}",
            Status: ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations.ToString(),
            FinalStatus: ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations.ToString(),
            ClaimEffect: ElectionDeploymentProofClaimEffect.AcceptedWithLimitations.ToString(),
            BlocksDeploymentProofClaims: false,
            ClaimSummary: "Deployment proof evidence is accepted with explicit limitations; the limitation is visible but does not by itself determine the election outcome.",
            DeploymentProfile: ElectionSelectableProfileCatalog.AdminOnlyProductionProfileId,
            DeploymentProtocolVersion: ElectionDeploymentProofConstants.DeploymentProtocolVersion,
            PublicCatalogRepository: null,
            PublicCatalogRef: "refs/tags/deployment-proof-v1",
            PublicCatalogCommit: null,
            PlatformCeremonyId: "ceremony-public-1",
            ActiveProofSetIdAtOpen: "server-proof-v1",
            OpenedAtUtc: observedAt.AddMinutes(-10),
            ClosedAtUtc: observedAt.AddMinutes(-5),
            FinalizedAtUtc: observedAt,
            VoidedAtUtc: null,
            LatestCheckpointId: checkpointId,
            CreatedAtUtc: observedAt.AddMinutes(-10),
            LastReconciledAtUtc: observedAt,
            ClaimLimitations:
            [
                "FEAT-144 WebClient proof handshake is not yet supported; complete WebClient proof binding remains downgraded for pilot handoff claims.",
            ],
            Checkpoints:
            [
                new ElectionDeploymentProofPublicCheckpointArtifactRecord(
                    checkpointId,
                    ElectionDeploymentProofCheckpointType.FinalPackageExport.ToString(),
                    ElectionLifecycleState.Finalized.ToString(),
                    ElectionLifecycleState.Finalized.ToString(),
                    TransitionArtifactId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"),
                    ReportPackageId: Guid.Parse("aaaaaaaa-0000-0000-0000-000000000004"),
                    ProofSetId: "server-proof-v1",
                    EvidenceStatus: ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations.ToString(),
                    ProviderStatus: ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations.ToString(),
                    ClaimEffect: ElectionDeploymentProofClaimEffect.AcceptedWithLimitations.ToString(),
                    ProviderErrorCodes: [],
                    SupersedesCheckpointId: null,
                    PublicSummary: "Deployment proof reconciled for final package export.",
                    SourceTransactionId: null,
                    SourceBlockHeight: null,
                    SourceBlockId: null,
                    ObservedAtUtc: observedAt),
            ],
            ComponentObservations:
            [
                new ElectionDeploymentProofPublicComponentObservationArtifactRecord(
                    Guid.Parse("aaaaaaaa-0000-0000-0000-000000000005"),
                    checkpointId,
                    ElectionDeploymentProofComponentId.HushServerNode.ToString(),
                    DeploymentProofId: "server-proof-v1",
                    ExpectedDeploymentProofId: "server-proof-v1",
                    ObservedDeploymentProofId: "server-proof-v1",
                    ExpectedArtifactHash: "sha256:" + Hash('a'),
                    ObservedArtifactHash: "sha256:" + Hash('a'),
                    EvidenceStatus: ElectionDeploymentProofEvidenceStatus.AcceptedWithLimitations.ToString(),
                    ObservationSource: ElectionDeploymentProofObservationSource.Fixture.ToString(),
                    SourceRef: "git:refs/tags/deployment-proof-v1",
                    ArtifactHash: "sha256:" + Hash('a'),
                    PackageHash: Hash('b'),
                    PublicPackageRef: "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
                    MismatchCode: null,
                    SupersedesProofIds: [],
                    ObservedAtUtc: observedAt),
                new ElectionDeploymentProofPublicComponentObservationArtifactRecord(
                    Guid.Parse("aaaaaaaa-0000-0000-0000-000000000006"),
                    checkpointId,
                    ElectionDeploymentProofComponentId.HushWebClient.ToString(),
                    DeploymentProofId: "webclient-proof-v1",
                    ExpectedDeploymentProofId: "webclient-proof-v1",
                    ObservedDeploymentProofId: null,
                    ExpectedArtifactHash: "sha256:" + Hash('c'),
                    ObservedArtifactHash: null,
                    EvidenceStatus: ElectionDeploymentProofEvidenceStatus.NotYetSupported.ToString(),
                    ObservationSource: ElectionDeploymentProofObservationSource.NotAvailable.ToString(),
                    SourceRef: "git:refs/tags/deployment-proof-v1",
                    ArtifactHash: "sha256:" + Hash('c'),
                    PackageHash: Hash('d'),
                    PublicPackageRef: "https://github.com/HushNetworkOrg/hush-deployment-proofs/tree/v1",
                    MismatchCode: ElectionDeploymentProofConstants.Feat144WebClientProofNotSupportedCode,
                    SupersedesProofIds: [],
                    ObservedAtUtc: observedAt),
            ],
            DeploymentEvents: [],
            ProofFamilies:
            [
                new ElectionDeploymentProofPublicProofFamilyArtifactRecord(
                    Guid.Parse("aaaaaaaa-0000-0000-0000-000000000007"),
                    checkpointId,
                    ElectionDeploymentProofConstants.RetentionLogPrivacyProofFamilyId,
                    "v1",
                    PackageId: "feat137-retention-log-privacy",
                    PackageHash: Hash('e'),
                    PromotedRegisterRef: "RDY-REG-v0.1.2",
                    ElectionDeploymentProofConstants.Feat137SourceFeature,
                    EvidenceStatus: ElectionDeploymentProofEvidenceStatus.Accepted.ToString(),
                    ClaimEffect: ElectionDeploymentProofClaimEffect.Accepted.ToString(),
                    MismatchCode: null,
                    PublicSummary: "Retention/log privacy proof-family remains accepted.",
                    ObservedAtUtc: observedAt),
            ],
            PublicPrivacyBoundary:
            [
                "no_private_key",
                "no_raw_runtime_log",
                "restricted_evidence_refs_are_ids_or_hashes_only",
            ]);
    }

    private static string HashBytesAsHex(byte[] value) =>
        Convert.ToHexString(value).ToLowerInvariant();

    private static string Hash(char value) =>
        new(char.ToLowerInvariant(value), 64);
}
