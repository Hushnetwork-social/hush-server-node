using FluentAssertions;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Elections;
using HushNode.Elections.gRPC;
using HushNode.MemPool;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using System.Data;
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace HushServerNode.Tests.Elections;

using SharedTrusteeReference = HushShared.Elections.Model.ElectionTrusteeReference;
using SharedResultOptionCount = HushShared.Elections.Model.ElectionResultOptionCount;
using SharedResultDenominatorEvidence = HushShared.Elections.Model.ElectionResultDenominatorEvidence;

public class ElectionQueryApplicationServiceTests
{
    [Fact]
    public async Task GetElectionAsync_WithExistingElection_ReturnsElectionDetailReadModelIncludingCeremonyData()
    {
        // Arrange
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection(acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]);
        var snapshot = ElectionModelFactory.CreateDraftSnapshot(
            election,
            snapshotReason: "initial draft",
            recordedByPublicAddress: "owner-address");
        var warning = ElectionModelFactory.CreateWarningAcknowledgement(
            election.ElectionId,
            ElectionWarningCode.LowAnonymitySet,
            election.CurrentDraftRevision,
            acknowledgedByPublicAddress: "owner-address");
        var invitation = ElectionModelFactory.CreateTrusteeInvitation(
            election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            invitedByPublicAddress: "owner-address",
            sentAtDraftRevision: election.CurrentDraftRevision);
        var acceptedInvitation = invitation.Accept(
            respondedAt: DateTime.UtcNow,
            resolvedAtDraftRevision: election.CurrentDraftRevision,
            lifecycleState: election.LifecycleState);
        var proposal = ElectionModelFactory.CreateGovernedProposal(
            election with { VoteAcceptanceLockedAt = DateTime.UtcNow },
            ElectionGovernedActionType.Close,
            proposedByPublicAddress: "owner-address");
        var approval = ElectionModelFactory.CreateGovernedProposalApproval(
            proposal,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            approvalNote: "Ready.");
        var boundaryArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            recordedByPublicAddress: "owner-address",
            trusteeSnapshot: ElectionModelFactory.CreateTrusteeBoundarySnapshot(
                requiredApprovalCount: election.RequiredApprovalCount!.Value,
                acceptedTrustees:
                [
                    new SharedTrusteeReference("trustee-a", "Alice"),
                ]),
            frozenEligibleVoterSetHash: [1, 2, 3],
            ceremonySnapshot: ElectionModelFactory.CreateCeremonyBindingSnapshot(
                Guid.NewGuid(),
                ceremonyVersionNumber: 1,
                profileId: "prod-1of1-v1",
                boundTrusteeCount: 1,
                requiredApprovalCount: 1,
                activeTrustees:
                [
                    new SharedTrusteeReference("trustee-a", "Alice"),
                ],
                tallyPublicKeyFingerprint: "tally-fingerprint"));
        var profile = ElectionModelFactory.CreateCeremonyProfile(
            "prod-1of1-v1",
            "Production 1 of 1",
            "Production profile",
            "provider-a",
            "v1",
            trusteeCount: 1,
            requiredApprovalCount: 1,
            devOnly: false);
        var ceremonyVersion = ElectionModelFactory.CreateCeremonyVersion(
                election.ElectionId,
                versionNumber: 1,
                profileId: profile.ProfileId,
                requiredApprovalCount: 1,
                boundTrustees:
                [
                    new SharedTrusteeReference("trustee-a", "Alice"),
                ],
                startedByPublicAddress: "owner-address")
            .MarkReady(DateTime.UtcNow, "tally-fingerprint");
        var transcriptEvent = ElectionModelFactory.CreateCeremonyTranscriptEvent(
            election.ElectionId,
            ceremonyVersion.Id,
            ceremonyVersion.VersionNumber,
            ElectionCeremonyTranscriptEventType.VersionReady,
            "Ceremony version became ready.",
            actorPublicAddress: "owner-address",
            tallyPublicKeyFingerprint: "tally-fingerprint");
        var activeTrusteeState = ElectionModelFactory.CreateCeremonyTrusteeState(
                election.ElectionId,
                ceremonyVersion.Id,
                "trustee-a",
                "Alice",
                ElectionTrusteeCeremonyState.AcceptedTrustee)
            .PublishTransportKey("transport-a", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow)
            .RecordSelfTestSuccess(DateTime.UtcNow)
            .RecordMaterialSubmitted(DateTime.UtcNow)
            .MarkCompleted(DateTime.UtcNow, "share-v1");
        var finalizationSession = ElectionModelFactory.CreateFinalizationSession(
            election,
            closeArtifactId: Guid.NewGuid(),
            acceptedBallotSetHash: [11, 12, 13],
            finalEncryptedTallyHash: [21, 22, 23],
            sessionPurpose: ElectionFinalizationSessionPurpose.Finalization,
            ceremonySnapshot: boundaryArtifact.CeremonySnapshot!,
            requiredShareCount: 1,
            eligibleTrustees:
            [
                new SharedTrusteeReference("trustee-a", "Alice"),
            ],
            createdByPublicAddress: "owner-address",
            governedProposalId: proposal.Id,
            latestTransactionId: Guid.NewGuid(),
            latestBlockHeight: 77,
            latestBlockId: Guid.NewGuid());
        var finalizationReleaseEvidence = ElectionModelFactory.CreateFinalizationReleaseEvidence(
            finalizationSession,
            acceptedTrustees:
            [
                new SharedTrusteeReference("trustee-a", "Alice"),
            ],
            completedByPublicAddress: "owner-address");
        var completedSession = finalizationSession.MarkCompleted(
            finalizationReleaseEvidence.Id,
            finalizationReleaseEvidence.CompletedAt,
            finalizationReleaseEvidence.SourceTransactionId,
            finalizationReleaseEvidence.SourceBlockHeight,
            finalizationReleaseEvidence.SourceBlockId);
        var finalizationShare = ElectionModelFactory.CreateAcceptedFinalizationShare(
            finalizationSessionId: completedSession.Id,
            electionId: election.ElectionId,
            trusteeUserAddress: "trustee-a",
            trusteeDisplayName: "Alice",
            submittedByPublicAddress: "trustee-a",
            shareIndex: 1,
            shareVersion: "share-v1",
            targetType: ElectionFinalizationTargetType.AggregateTally,
            claimedCloseArtifactId: completedSession.CloseArtifactId,
            claimedAcceptedBallotSetHash: completedSession.AcceptedBallotSetHash,
            claimedFinalEncryptedTallyHash: completedSession.FinalEncryptedTallyHash,
            claimedTargetTallyId: completedSession.TargetTallyId,
            claimedCeremonyVersionId: boundaryArtifact.CeremonySnapshot!.CeremonyVersionId,
            claimedTallyPublicKeyFingerprint: boundaryArtifact.CeremonySnapshot.TallyPublicKeyFingerprint,
            shareMaterial: "ciphertext-share-material");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetLatestDraftSnapshotAsync(election.ElectionId)).ReturnsAsync(snapshot);
            repo.Setup(x => x.GetWarningAcknowledgementsAsync(election.ElectionId)).ReturnsAsync([warning]);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync([acceptedInvitation]);
            repo.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId)).ReturnsAsync([boundaryArtifact]);
            repo.Setup(x => x.GetGovernedProposalsAsync(election.ElectionId)).ReturnsAsync([proposal]);
            repo.Setup(x => x.GetGovernedProposalApprovalsAsync(proposal.Id)).ReturnsAsync([approval]);
            repo.Setup(x => x.GetCeremonyProfilesAsync()).ReturnsAsync([profile]);
            repo.Setup(x => x.GetCeremonyVersionsAsync(election.ElectionId)).ReturnsAsync([ceremonyVersion]);
            repo.Setup(x => x.GetActiveCeremonyVersionAsync(election.ElectionId)).ReturnsAsync(ceremonyVersion);
            repo.Setup(x => x.GetCeremonyTranscriptEventsAsync(ceremonyVersion.Id)).ReturnsAsync([transcriptEvent]);
            repo.Setup(x => x.GetCeremonyTrusteeStatesAsync(ceremonyVersion.Id)).ReturnsAsync([activeTrusteeState]);
            repo.Setup(x => x.GetFinalizationSessionsAsync(election.ElectionId)).ReturnsAsync([completedSession]);
            repo.Setup(x => x.GetFinalizationSharesAsync(completedSession.Id)).ReturnsAsync([finalizationShare]);
            repo.Setup(x => x.GetFinalizationReleaseEvidenceRecordsAsync(election.ElectionId)).ReturnsAsync([finalizationReleaseEvidence]);
        });

        var sut = CreateQueryService(mocker);

        // Act
        var response = await sut.GetElectionAsync(election.ElectionId);

        // Assert
        response.Success.Should().BeTrue();
        response.Election.Should().NotBeNull();
        response.Election.Title.Should().Be("Board Election");
        response.LatestDraftSnapshot.Should().NotBeNull();
        response.LatestDraftSnapshot.DraftRevision.Should().Be(1);
        response.WarningAcknowledgements.Should().ContainSingle();
        response.WarningAcknowledgements[0].WarningCode.Should().Be(ElectionWarningCodeProto.LowAnonymitySet);
        response.TrusteeInvitations.Should().ContainSingle();
        response.BoundaryArtifacts.Should().ContainSingle();
        response.BoundaryArtifacts[0].CeremonySnapshot.Should().NotBeNull();
        response.BoundaryArtifacts[0].CeremonySnapshot!.TallyPublicKeyFingerprint.Should().Be("tally-fingerprint");
        response.GovernedProposals.Should().ContainSingle();
        response.GovernedProposals[0].ActionType.Should().Be(ElectionGovernedActionTypeProto.GovernedActionClose);
        response.GovernedProposalApprovals.Should().ContainSingle();
        response.GovernedProposalApprovals[0].ApprovalNote.Should().Be("Ready.");
        response.CeremonyProfiles.Should().ContainSingle();
        response.CeremonyVersions.Should().ContainSingle();
        response.CeremonyVersions[0].Status.Should().Be(ElectionCeremonyVersionStatusProto.CeremonyVersionReady);
        response.CeremonyTranscriptEvents.Should().ContainSingle();
        response.ActiveCeremonyTrusteeStates.Should().ContainSingle();
        response.ActiveCeremonyTrusteeStates[0].State.Should().Be(ElectionTrusteeCeremonyStateProto.CeremonyStateCompleted);
        response.FinalizationSessions.Should().ContainSingle();
        response.FinalizationSessions[0].Status.Should().Be(ElectionFinalizationSessionStatusProto.FinalizationSessionCompleted);
        response.FinalizationSessions[0].RequiredShareCount.Should().Be(1);
        response.FinalizationShares.Should().ContainSingle();
        response.FinalizationShares[0].Status.Should().Be(ElectionFinalizationShareStatusProto.FinalizationShareAccepted);
        response.FinalizationShares[0].FailureCode.Should().BeEmpty();
        response.FinalizationReleaseEvidenceRecords.Should().ContainSingle();
        response.FinalizationReleaseEvidenceRecords[0].AcceptedShareCount.Should().Be(1);
    }

    [Fact]
    public async Task GetElectionAsync_WithMissingElection_ReturnsNotFoundResponse()
    {
        // Arrange
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(electionId)).ReturnsAsync((ElectionRecord?)null);
        });

        var sut = CreateQueryService(mocker);

        // Act
        var response = await sut.GetElectionAsync(electionId);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorMessage.Should().Contain(electionId.ToString());
    }

    [Fact]
    public async Task GetElectionAsync_WithLegacyAdminOnlyOpenBoundary_PersistsSyntheticProtectedTallyBinding()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-10);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
            OpenArtifactId = Guid.NewGuid(),
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            recordedByPublicAddress: "owner-address",
            recordedAt: openedAt,
            frozenEligibleVoterSetHash: [1, 2, 3, 4]) with
        {
            Id = election.OpenArtifactId!.Value,
        };
        var expectedBinding = ElectionProtectedTallyBinding.BuildAdminOnlyProtectedTallyBindingSnapshot(election);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId)).ReturnsAsync([openArtifact]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionAsync(election.ElectionId);

        response.Success.Should().BeTrue();
        response.BoundaryArtifacts.Should().ContainSingle();
        response.BoundaryArtifacts[0].CeremonySnapshot.Should().NotBeNull();
        response.BoundaryArtifacts[0].CeremonySnapshot!.ProfileId.Should().Be(expectedBinding.ProfileId);
        response.BoundaryArtifacts[0].CeremonySnapshot!.CeremonyVersionId.Should().Be(expectedBinding.CeremonyVersionId.ToString());
        response.BoundaryArtifacts[0].CeremonySnapshot!.TallyPublicKeyFingerprint.Should().Be(expectedBinding.TallyPublicKeyFingerprint);

        mocker.GetMock<IElectionsRepository>()
            .Verify(
                x => x.UpdateBoundaryArtifactAsync(It.Is<ElectionBoundaryArtifactRecord>(artifact =>
                    artifact.Id == openArtifact.Id &&
                    artifact.CeremonySnapshot != null &&
                    artifact.CeremonySnapshot.ProfileId == expectedBinding.ProfileId &&
                    artifact.CeremonySnapshot.TallyPublicKeyFingerprint == expectedBinding.TallyPublicKeyFingerprint)),
                Times.Once);
        mocker.GetMock<IWritableUnitOfWork<ElectionsDbContext>>()
            .Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task GetElectionsByOwnerAsync_WithOwnerAddress_ReturnsSummaries()
    {
        // Arrange
        var mocker = new AutoMocker();
        var elections = new[]
        {
            CreateAdminElection(title: "Board Election"),
            CreateAdminElection(title: "Treasurer Vote"),
        };

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionsByOwnerAsync("owner-address")).ReturnsAsync(elections);
        });

        var sut = CreateQueryService(mocker);

        // Act
        var response = await sut.GetElectionsByOwnerAsync("owner-address");

        // Assert
        response.Elections.Should().HaveCount(2);
        response.Elections.Select(x => x.Title).Should().Equal("Board Election", "Treasurer Vote");
    }

    [Fact]
    public async Task SearchElectionDirectoryAsync_WithSearchInputs_ReturnsElectionSummaries()
    {
        var mocker = new AutoMocker();
        var elections = new[]
        {
            CreateAdminElection(title: "Board Election"),
            CreateAdminElection(title: "Budget Election"),
        };

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.SearchElectionsAsync(
                    "board",
                    It.Is<IReadOnlyCollection<string>>(addresses =>
                        addresses.Count == 1 && addresses.Contains("owner-address")),
                    12))
                .ReturnsAsync(elections);
            repo.Setup(x => x.GetRosterEntriesByLinkedActorAsync("search-actor"))
                .ReturnsAsync(Array.Empty<ElectionRosterEntryRecord>());
            repo.Setup(x => x.GetReportAccessGrantsByActorAsync("search-actor"))
                .ReturnsAsync(Array.Empty<ElectionReportAccessGrantRecord>());
            repo.Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync("search-actor"))
                .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.SearchElectionDirectoryAsync(
            "  board  ",
            ["owner-address", "owner-address", " "],
            12,
            "search-actor");

        response.Success.Should().BeTrue();
        response.ErrorMessage.Should().BeEmpty();
        response.SearchTerm.Should().Be("board");
        response.ActorPublicAddress.Should().Be("search-actor");
        response.Elections.Select(x => x.Title).Should().Equal("Board Election", "Budget Election");
        response.Entries.Should().HaveCount(2);
        response.Entries.Should().OnlyContain(x => x.CanOpenEligibility);
    }

    [Fact]
    public async Task SearchElectionDirectoryAsync_WithEmptyInputs_ReturnsEmptyResponseWithoutQueryingRepository()
    {
        var mocker = new AutoMocker();

        ConfigureReadOnlyRepository(mocker, _ => { });

        var sut = CreateQueryService(mocker);

        var response = await sut.SearchElectionDirectoryAsync("   ", [" ", ""], 20, "search-actor");

        response.Success.Should().BeTrue();
        response.ErrorMessage.Should().BeEmpty();
        response.SearchTerm.Should().BeEmpty();
        response.ActorPublicAddress.Should().Be("search-actor");
        response.Elections.Should().BeEmpty();
        mocker.GetMock<IElectionsRepository>()
            .Verify(x => x.SearchElectionsAsync(It.IsAny<string>(), It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<int>()), Times.Never);
    }

    [Fact]
    public async Task SearchElectionDirectoryAsync_WithActorRolesAndFinalizedElection_ReturnsRoleBadgesAndExpectedEligibilityRules()
    {
        var mocker = new AutoMocker();
        var linkedFinalizedElection = CreateAdminElection(title: "Linked Finalized Election") with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
        };
        var ownerClaimableElection = CreateAdminElection(title: "Owner Claimable Election") with
        {
            OwnerPublicAddress = "actor-address",
        };
        var unlinkedFinalizedElection = CreateAdminElection(title: "Unlinked Finalized Election") with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
        };
        var auditorElection = CreateAdminElection(title: "Auditor Election");
        var claimableElection = CreateAdminElection(title: "Claimable Election");
        var voterRosterEntry = ElectionModelFactory.CreateRosterEntry(
                linkedFinalizedElection.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter@example.org",
                ElectionVotingRightStatus.Active)
            .LinkToActor("actor-address", DateTime.UtcNow);
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            auditorElection.ElectionId,
            "owner-address",
            "actor-address");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.SearchElectionsAsync(
                    "admin",
                    It.IsAny<IReadOnlyCollection<string>>(),
                    12))
                .ReturnsAsync([linkedFinalizedElection, ownerClaimableElection, unlinkedFinalizedElection, auditorElection, claimableElection]);
            repo.Setup(x => x.GetRosterEntriesByLinkedActorAsync("actor-address"))
                .ReturnsAsync([voterRosterEntry]);
            repo.Setup(x => x.GetReportAccessGrantsByActorAsync("actor-address"))
                .ReturnsAsync([auditorGrant]);
            repo.Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync("actor-address"))
                .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.SearchElectionDirectoryAsync("admin", Array.Empty<string>(), 12, "actor-address");

        response.Entries.Should().HaveCount(5);

        var linkedFinalizedEntry = response.Entries.Single(x => x.Election.Title == "Linked Finalized Election");
        linkedFinalizedEntry.ActorRoles.IsVoter.Should().BeTrue();
        linkedFinalizedEntry.CanOpenEligibility.Should().BeFalse();
        linkedFinalizedEntry.EligibilityDisabledReason.Should().Be("This election is already linked to this Hush account.");

        var ownerClaimableEntry = response.Entries.Single(x => x.Election.Title == "Owner Claimable Election");
        ownerClaimableEntry.ActorRoles.IsOwnerAdmin.Should().BeTrue();
        ownerClaimableEntry.ActorRoles.IsVoter.Should().BeFalse();
        ownerClaimableEntry.CanOpenEligibility.Should().BeTrue();
        ownerClaimableEntry.EligibilityDisabledReason.Should().BeEmpty();

        var unlinkedFinalizedEntry = response.Entries.Single(x => x.Election.Title == "Unlinked Finalized Election");
        unlinkedFinalizedEntry.ActorRoles.IsVoter.Should().BeFalse();
        unlinkedFinalizedEntry.ActorRoles.IsDesignatedAuditor.Should().BeFalse();
        unlinkedFinalizedEntry.CanOpenEligibility.Should().BeFalse();
        unlinkedFinalizedEntry.EligibilityDisabledReason.Should().Be("Claim-link discovery is unavailable after finalization.");

        var auditorEntry = response.Entries.Single(x => x.Election.Title == "Auditor Election");
        auditorEntry.ActorRoles.IsDesignatedAuditor.Should().BeTrue();
        auditorEntry.CanOpenEligibility.Should().BeFalse();
        auditorEntry.EligibilityDisabledReason.Should().Be("This election is already linked to this Hush account.");

        var claimableEntry = response.Entries.Single(x => x.Election.Title == "Claimable Election");
        claimableEntry.ActorRoles.IsVoter.Should().BeFalse();
        claimableEntry.ActorRoles.IsDesignatedAuditor.Should().BeFalse();
        claimableEntry.CanOpenEligibility.Should().BeTrue();
        claimableEntry.EligibilityDisabledReason.Should().BeEmpty();
    }

    [Fact]
    public async Task GetElectionHubViewAsync_WithResolvedActorRoles_ReturnsLifecycleSortedRoleAwareEntries()
    {
        var mocker = new AutoMocker();
        var now = DateTime.UtcNow;
        var voterElection = CreateAdminElection("Open Voter Election") with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = now.AddMinutes(-20),
            LastUpdatedAt = now.AddMinutes(-20),
        };
        var voterRosterEntry = ElectionModelFactory.CreateRosterEntry(
                voterElection.ElectionId,
                "5001",
                ElectionRosterContactType.Email,
                "voter@example.org",
                ElectionVotingRightStatus.Active,
                importedAt: now.AddHours(-1))
            .FreezeAtOpen(voterElection.OpenedAt!.Value)
            .LinkToActor("actor-address", now.AddMinutes(-19));
        var ownerElection = CreateAdminElection("Draft Owner Election") with
        {
            OwnerPublicAddress = "actor-address",
            LastUpdatedAt = now.AddMinutes(-15),
        };
        var trusteeElection = CreateTrusteeElection("Closed Trustee Election") with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            ClosedAt = now.AddMinutes(-10),
            ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
            LastUpdatedAt = now.AddMinutes(-10),
        };
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                trusteeElection.ElectionId,
                trusteeUserAddress: "actor-address",
                trusteeDisplayName: "Actor Trustee",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: trusteeElection.CurrentDraftRevision)
            .Accept(
                respondedAt: now.AddDays(-1),
                resolvedAtDraftRevision: trusteeElection.CurrentDraftRevision,
                lifecycleState: ElectionLifecycleState.Draft);
        var pendingProposal = ElectionModelFactory.CreateGovernedProposal(
            trusteeElection,
            ElectionGovernedActionType.Finalize,
            proposedByPublicAddress: "owner-address");
        var auditorElection = CreateAdminElection("Finalized Auditor Election") with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = now.AddMinutes(-5),
            OfficialResultArtifactId = Guid.NewGuid(),
            LastUpdatedAt = now.AddMinutes(-5),
        };
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            auditorElection.ElectionId,
            actorPublicAddress: "actor-address",
            grantedByPublicAddress: "owner-address");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionsByOwnerAsync("actor-address"))
                .ReturnsAsync([ownerElection]);
            repo.Setup(x => x.GetReportAccessGrantsByActorAsync("actor-address"))
                .ReturnsAsync([auditorGrant]);
            repo.Setup(x => x.GetRosterEntriesByLinkedActorAsync("actor-address"))
                .ReturnsAsync([voterRosterEntry]);
            repo.Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync("actor-address"))
                .ReturnsAsync([acceptedInvitation]);
            repo.Setup(x => x.GetElectionsByIdsAsync(It.IsAny<IReadOnlyCollection<ElectionId>>()))
                .ReturnsAsync([trusteeElection, auditorElection, ownerElection, voterElection]);
            repo.Setup(x => x.GetParticipationRecordAsync(voterElection.ElectionId, "5001"))
                .ReturnsAsync((ElectionParticipationRecord?)null);
            repo.Setup(x => x.GetPendingGovernedProposalAsync(trusteeElection.ElectionId))
                .ReturnsAsync(pendingProposal);
            repo.Setup(x => x.GetGovernedProposalApprovalsAsync(pendingProposal.Id))
                .ReturnsAsync(Array.Empty<ElectionGovernedProposalApprovalRecord>());
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionHubViewAsync("actor-address");

        response.Success.Should().BeTrue();
        response.HasAnyElectionRoles.Should().BeTrue();
        response.EmptyStateReason.Should().BeEmpty();
        response.Elections.Select(x => x.Election.Title).Should().Equal(
            "Open Voter Election",
            "Draft Owner Election",
            "Closed Trustee Election",
            "Finalized Auditor Election");

        var voterEntry = response.Elections.Single(x => x.Election.Title == "Open Voter Election");
        voterEntry.ActorRoles.IsVoter.Should().BeTrue();
        voterEntry.SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionVoterCastBallot);
        voterEntry.CanViewParticipantResults.Should().BeTrue();

        var ownerEntry = response.Elections.Single(x => x.Election.Title == "Draft Owner Election");
        ownerEntry.ActorRoles.IsOwnerAdmin.Should().BeTrue();
        ownerEntry.SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionOwnerManageDraft);

        var trusteeEntry = response.Elections.Single(x => x.Election.Title == "Closed Trustee Election");
        trusteeEntry.ActorRoles.IsTrustee.Should().BeTrue();
        trusteeEntry.SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionTrusteeApproveGovernedAction);

        var auditorEntry = response.Elections.Single(x => x.Election.Title == "Finalized Auditor Election");
        auditorEntry.ActorRoles.IsDesignatedAuditor.Should().BeTrue();
        auditorEntry.SuggestedAction.Should().Be(ElectionHubNextActionHintProto.ElectionHubActionAuditorReviewPackage);
        auditorEntry.CanViewNamedParticipationRoster.Should().BeTrue();
        auditorEntry.CanViewReportPackage.Should().BeTrue();
        auditorEntry.CanViewParticipantResults.Should().BeTrue();
    }

    [Fact]
    public async Task GetElectionHubViewAsync_WithTrusteeResultVisibility_ReturnsTrusteeReviewAction()
    {
        var mocker = new AutoMocker();
        var finalizedTrusteeElection = CreateTrusteeElection("Finalized Trustee Election") with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-2),
            OfficialResultArtifactId = Guid.NewGuid(),
            LastUpdatedAt = DateTime.UtcNow.AddMinutes(-2),
        };
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                finalizedTrusteeElection.ElectionId,
                trusteeUserAddress: "trustee-address",
                trusteeDisplayName: "Trustee",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: finalizedTrusteeElection.CurrentDraftRevision)
            .Accept(
                respondedAt: DateTime.UtcNow.AddMinutes(-10),
                resolvedAtDraftRevision: finalizedTrusteeElection.CurrentDraftRevision,
                lifecycleState: ElectionLifecycleState.Draft);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync("trustee-address"))
                .ReturnsAsync([acceptedInvitation]);
            repo.Setup(x => x.GetElectionsByIdsAsync(It.IsAny<IReadOnlyCollection<ElectionId>>()))
                .ReturnsAsync([finalizedTrusteeElection]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionHubViewAsync("trustee-address");

        response.Success.Should().BeTrue();
        response.Elections.Should().ContainSingle();
        response.Elections[0].ActorRoles.IsTrustee.Should().BeTrue();
        response.Elections[0].SuggestedAction.Should().Be(
            ElectionHubNextActionHintProto.ElectionHubActionTrusteeReviewResult);
    }

    [Fact]
    public async Task GetElectionEnvelopeAccessAsync_WithStoredAccess_ReturnsWrappedElectionKey()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var accessRecord = new ElectionEnvelopeAccessRecord(
            electionId,
            "trustee-address",
            "node-wrapped-election-private-key",
            "wrapped-election-private-key",
            DateTime.UtcNow,
            Guid.NewGuid(),
            12,
            Guid.NewGuid());

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionEnvelopeAccessAsync(electionId, "trustee-address"))
                .ReturnsAsync(accessRecord);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionEnvelopeAccessAsync(electionId, "trustee-address");

        response.Success.Should().BeTrue();
        response.ActorEncryptedElectionPrivateKey.Should().Be("wrapped-election-private-key");
    }

    [Fact]
    public async Task GetElectionEligibilityViewAsync_WithOwnerRole_ReturnsRestrictedEligibilityData()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-30);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            EligibilityMutationPolicy = EligibilityMutationPolicy.LateActivationForRosteredVotersOnly,
            OpenedAt = openedAt,
            OpenArtifactId = Guid.NewGuid(),
            LastUpdatedAt = openedAt,
        };
        var linkedActiveEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Phone,
                "+41790001001",
                ElectionVotingRightStatus.Active,
                importedAt: openedAt.AddHours(-2))
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));
        var lateActivatedEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1002",
                ElectionRosterContactType.Email,
                "voter-1002@example.org",
                ElectionVotingRightStatus.Inactive,
                importedAt: openedAt.AddHours(-2))
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-two", openedAt.AddMinutes(2))
            .MarkVotingRightActive("owner-address", openedAt.AddMinutes(5));
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            "1001",
            ElectionParticipationStatus.CountedAsVoted,
            openedAt.AddMinutes(10));
        var activationEvent = ElectionModelFactory.CreateEligibilityActivationEvent(
            election.ElectionId,
            "1002",
            "owner-address",
            ElectionEligibilityActivationOutcome.Activated,
            occurredAt: openedAt.AddMinutes(5));
        var snapshot = ElectionModelFactory.CreateEligibilitySnapshot(
            election.ElectionId,
            ElectionEligibilitySnapshotType.Open,
            election.EligibilityMutationPolicy,
            rosteredCount: 2,
            linkedCount: 2,
            activeDenominatorCount: 1,
            countedParticipationCount: 0,
            blankCount: 0,
            didNotVoteCount: 1,
            rosteredVoterSetHash: [1, 2, 3],
            activeDenominatorSetHash: [4, 5, 6],
            countedParticipationSetHash: [7, 8, 9],
            recordedByPublicAddress: "owner-address",
            boundaryArtifactId: election.OpenArtifactId,
            recordedAt: openedAt);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntriesAsync(election.ElectionId)).ReturnsAsync([linkedActiveEntry, lateActivatedEntry]);
            repo.Setup(x => x.GetParticipationRecordsAsync(election.ElectionId)).ReturnsAsync([participation]);
            repo.Setup(x => x.GetEligibilityActivationEventsAsync(election.ElectionId)).ReturnsAsync([activationEvent]);
            repo.Setup(x => x.GetEligibilitySnapshotsAsync(election.ElectionId)).ReturnsAsync([snapshot]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionEligibilityViewAsync(election.ElectionId, "owner-address");

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorOwner);
        response.CanImportRoster.Should().BeFalse();
        response.CanActivateRoster.Should().BeTrue();
        response.CanReviewRestrictedRoster.Should().BeTrue();
        response.CanClaimIdentity.Should().BeFalse();
        response.UsesTemporaryVerificationCode.Should().BeTrue();
        response.TemporaryVerificationCode.Should().Be("1111");
        response.RestrictedRosterEntries.Should().HaveCount(2);
        response.ActivationEvents.Should().ContainSingle();
        response.EligibilitySnapshots.Should().ContainSingle();
        response.Summary.RosteredCount.Should().Be(2);
        response.Summary.LinkedCount.Should().Be(2);
        response.Summary.ActiveCount.Should().Be(2);
        response.Summary.CurrentDenominatorCount.Should().Be(2);
        response.Summary.CountedParticipationCount.Should().Be(1);
        response.Summary.DidNotVoteCount.Should().Be(1);
    }

    [Fact]
    public async Task GetElectionEligibilityViewAsync_WithLinkedVoterRole_ReturnsSelfStatusOnly()
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection();
        var linkedEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .LinkToActor("voter-address", DateTime.UtcNow);
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            "1001",
            ElectionParticipationStatus.Blank);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntriesAsync(election.ElectionId)).ReturnsAsync([linkedEntry]);
            repo.Setup(x => x.GetParticipationRecordsAsync(election.ElectionId)).ReturnsAsync([participation]);
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
                .ReturnsAsync(linkedEntry);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionEligibilityViewAsync(election.ElectionId, "voter-address");

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorLinkedVoter);
        response.CanReviewRestrictedRoster.Should().BeFalse();
        response.CanClaimIdentity.Should().BeFalse();
        response.SelfRosterEntry.Should().NotBeNull();
        response.SelfRosterEntry.OrganizationVoterId.Should().Be("1001");
        response.SelfRosterEntry.ParticipationStatus.Should().Be(ElectionParticipationStatusProto.ParticipationBlank);
        response.RestrictedRosterEntries.Should().BeEmpty();
        response.ActivationEvents.Should().BeEmpty();
        response.EligibilitySnapshots.Should().BeEmpty();
    }

    [Fact]
    public async Task GetElectionEligibilityViewAsync_WithDesignatedAuditorRole_ReturnsRestrictedRosterData()
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
        };
        var linkedEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .LinkToActor("voter-address", DateTime.UtcNow);
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            "1001",
            ElectionParticipationStatus.Blank);
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            actorPublicAddress: "auditor-address",
            grantedByPublicAddress: "owner-address");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntriesAsync(election.ElectionId)).ReturnsAsync([linkedEntry]);
            repo.Setup(x => x.GetParticipationRecordsAsync(election.ElectionId)).ReturnsAsync([participation]);
            repo.Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, "auditor-address"))
                .ReturnsAsync(auditorGrant);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionEligibilityViewAsync(election.ElectionId, "auditor-address");

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorRestrictedReviewer);
        response.CanReviewRestrictedRoster.Should().BeTrue();
        response.RestrictedRosterEntries.Should().ContainSingle();
        response.RestrictedRosterEntries[0].OrganizationVoterId.Should().Be("1001");
    }

    [Fact]
    public async Task GetElectionEligibilityViewAsync_WithAcceptedTrusteeRole_RemainsReadOnly()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection();
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                election.ElectionId,
                trusteeUserAddress: "trustee-address",
                trusteeDisplayName: "Trustee",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: election.CurrentDraftRevision)
            .Accept(
                respondedAt: DateTime.UtcNow,
                resolvedAtDraftRevision: election.CurrentDraftRevision,
                lifecycleState: ElectionLifecycleState.Draft);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync([acceptedInvitation]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionEligibilityViewAsync(election.ElectionId, "trustee-address");

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorReadOnly);
        response.CanReviewRestrictedRoster.Should().BeFalse();
        response.RestrictedRosterEntries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetElectionEligibilityViewAsync_WithClosedElectionUnlinkedActor_AllowsClaimIdentity()
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            ClosedProgressStatus = ElectionClosedProgressStatus.WaitingForTrusteeShares,
        };

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionEligibilityViewAsync(election.ElectionId, "unlinked-voter");

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionEligibilityActorRoleProto.EligibilityActorReadOnly);
        response.CanClaimIdentity.Should().BeTrue();
        response.CanReviewRestrictedRoster.Should().BeFalse();
    }

    [Fact]
    public async Task GetElectionVotingViewAsync_WithPendingSubmissionKey_ReturnsCurrentVotingReadModel()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));
        var commitmentRegistration = ElectionModelFactory.CreateCommitmentRegistrationRecord(
            election.ElectionId,
            "1001",
            "voter-address",
            "commitment-hash-1",
            openedAt.AddMinutes(2));
        var ceremonySnapshot = ElectionModelFactory.CreateCeremonyBindingSnapshot(
            Guid.NewGuid(),
            ceremonyVersionNumber: 1,
            profileId: "dkg-prod-1of1",
            boundTrusteeCount: 1,
            requiredApprovalCount: 1,
            activeTrustees:
            [
                new SharedTrusteeReference("trustee-a", "Alice"),
            ],
            tallyPublicKeyFingerprint: "tally-fingerprint");
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election with { OpenArtifactId = Guid.NewGuid() },
            recordedByPublicAddress: "owner-address",
            recordedAt: openedAt,
            frozenEligibleVoterSetHash: [1, 2, 3, 4],
            ceremonySnapshot: ceremonySnapshot);
        election = election with
        {
            OpenArtifactId = openArtifact.Id,
        };

        var signedPendingEnvelope = new HushShared.Blockchain.TransactionModel.States.SignedTransaction<EncryptedElectionEnvelopePayload>(
            EncryptedElectionEnvelopePayloadHandler.CreateNew(
                election.ElectionId,
                EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
                "node-envelope",
                "actor-envelope",
                "encrypted-payload"),
            new HushShared.Blockchain.Model.SignatureInfo("voter-address", "signature"));
        var pendingTransaction = new HushShared.Blockchain.TransactionModel.States.ValidatedTransaction<EncryptedElectionEnvelopePayload>(
            signedPendingEnvelope,
            new HushShared.Blockchain.Model.SignatureInfo("validator-address", "signature"));
        var pendingEnvelope = new DecryptedElectionEnvelope<
            HushShared.Blockchain.TransactionModel.States.ValidatedTransaction<EncryptedElectionEnvelopePayload>>(
            pendingTransaction,
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast,
            System.Text.Json.JsonSerializer.Serialize(new AcceptElectionBallotCastActionPayload(
                "voter-address",
                "same-election-key",
                "ciphertext",
                "proof",
                "nullifier-1",
                openArtifact.Id,
                [1, 2, 3, 4],
                ceremonySnapshot.CeremonyVersionId,
                ceremonySnapshot.ProfileId,
                ceremonySnapshot.TallyPublicKeyFingerprint)));

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
                .ReturnsAsync(rosterEntry);
            repo.Setup(x => x.GetCommitmentRegistrationByLinkedActorAsync(election.ElectionId, "voter-address"))
                .ReturnsAsync(commitmentRegistration);
            repo.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId)).ReturnsAsync([openArtifact]);
        });

        mocker.GetMock<IMemPoolService>()
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns([pendingTransaction]);
        mocker.GetMock<IElectionEnvelopeCryptoService>()
            .Setup(x => x.TryDecryptValidated(pendingTransaction))
            .Returns(pendingEnvelope);

        var sut = CreateQueryService(
            mocker,
            memPoolService: mocker.GetMock<IMemPoolService>().Object,
            electionEnvelopeCryptoService: mocker.GetMock<IElectionEnvelopeCryptoService>().Object);

        var response = await sut.GetElectionVotingViewAsync(
            election.ElectionId,
            "voter-address",
            "same-election-key");

        response.Success.Should().BeTrue();
        response.CommitmentRegistered.Should().BeTrue();
        response.HasCommitmentRegisteredAt.Should().BeTrue();
        response.SubmissionStatus.Should().Be(ElectionVotingSubmissionStatusProto.VotingSubmissionStatusStillProcessing);
        response.OpenArtifactId.Should().Be(openArtifact.Id.ToString());
        response.EligibleSetHash.Should().NotBeEmpty();
        response.CeremonyVersionId.Should().Be(ceremonySnapshot.CeremonyVersionId.ToString());
        response.DkgProfileId.Should().Be(ceremonySnapshot.ProfileId);
        response.TallyPublicKeyFingerprint.Should().Be(ceremonySnapshot.TallyPublicKeyFingerprint);
    }

    [Fact]
    public async Task GetElectionVotingViewAsync_WithLegacyAdminOnlyOpenBoundaryWithoutStoredSnapshot_ReturnsSyntheticProtectedTallyBinding()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election with { OpenArtifactId = Guid.NewGuid() },
            recordedByPublicAddress: "owner-address",
            recordedAt: openedAt,
            frozenEligibleVoterSetHash: [1, 2, 3, 4]);
        election = election with
        {
            OpenArtifactId = openArtifact.Id,
        };
        var expectedBinding = ElectionProtectedTallyBinding.BuildAdminOnlyProtectedTallyBindingSnapshot(election);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId)).ReturnsAsync([openArtifact]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionVotingViewAsync(
            election.ElectionId,
            "voter-address",
            submissionIdempotencyKey: null);

        response.Success.Should().BeTrue();
        response.OpenArtifactId.Should().Be(openArtifact.Id.ToString());
        response.EligibleSetHash.Should().NotBeEmpty();
        response.CeremonyVersionId.Should().Be(expectedBinding.CeremonyVersionId.ToString());
        response.DkgProfileId.Should().Be(expectedBinding.ProfileId);
        response.TallyPublicKeyFingerprint.Should().Be(expectedBinding.TallyPublicKeyFingerprint);

        mocker.GetMock<IElectionsRepository>()
            .Verify(
                x => x.UpdateBoundaryArtifactAsync(It.Is<ElectionBoundaryArtifactRecord>(artifact =>
                    artifact.Id == openArtifact.Id &&
                    artifact.CeremonySnapshot != null &&
                    artifact.CeremonySnapshot.ProfileId == expectedBinding.ProfileId &&
                    artifact.CeremonySnapshot.TallyPublicKeyFingerprint == expectedBinding.TallyPublicKeyFingerprint)),
                Times.Once);
        mocker.GetMock<IWritableUnitOfWork<ElectionsDbContext>>()
            .Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task GetElectionVotingViewAsync_WithCommittedSubmissionKey_ReturnsAlreadyUsedAndAcceptedAt()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            "1001",
            ElectionParticipationStatus.CountedAsVoted,
            openedAt.AddMinutes(4));
        var checkoffConsumption = ElectionModelFactory.CreateCheckoffConsumptionRecord(
            election.ElectionId,
            "1001",
            openedAt.AddMinutes(4));
        var idempotencyRecord = ElectionModelFactory.CreateCastIdempotencyRecord(
            election.ElectionId,
            ComputeScopedHash("used-election-key"),
            openedAt.AddMinutes(4));

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
                .ReturnsAsync(rosterEntry);
            repo.Setup(x => x.GetParticipationRecordAsync(election.ElectionId, "1001"))
                .ReturnsAsync(participation);
            repo.Setup(x => x.GetCheckoffConsumptionAsync(election.ElectionId, "1001"))
                .ReturnsAsync(checkoffConsumption);
            repo.Setup(x => x.GetCastIdempotencyRecordAsync(election.ElectionId, idempotencyRecord.IdempotencyKeyHash))
                .ReturnsAsync(idempotencyRecord);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionVotingViewAsync(
            election.ElectionId,
            "voter-address",
            "used-election-key");

        response.Success.Should().BeTrue();
        response.PersonalParticipationStatus.Should().Be(ElectionParticipationStatusProto.ParticipationCountedAsVoted);
        response.SubmissionStatus.Should().Be(ElectionVotingSubmissionStatusProto.VotingSubmissionStatusAlreadyUsed);
        response.HasAcceptedAt.Should().BeTrue();
    }

    [Fact]
    public async Task GetElectionVotingViewAsync_WithCommittedSubmissionKeyCacheHit_ReturnsAlreadyUsedWithoutRepositoryLookup()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            "1001",
            ElectionParticipationStatus.CountedAsVoted,
            openedAt.AddMinutes(4));
        var checkoffConsumption = ElectionModelFactory.CreateCheckoffConsumptionRecord(
            election.ElectionId,
            "1001",
            openedAt.AddMinutes(4));
        var idempotencyKeyHash = ComputeScopedHash("cached-election-key");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
                .ReturnsAsync(rosterEntry);
            repo.Setup(x => x.GetParticipationRecordAsync(election.ElectionId, "1001"))
                .ReturnsAsync(participation);
            repo.Setup(x => x.GetCheckoffConsumptionAsync(election.ElectionId, "1001"))
                .ReturnsAsync(checkoffConsumption);
        });

        mocker.GetMock<IElectionCastIdempotencyCacheService>()
            .Setup(x => x.ExistsAsync(election.ElectionId.ToString(), idempotencyKeyHash))
            .ReturnsAsync(true);

        var sut = CreateQueryService(
            mocker,
            castIdempotencyCacheService: mocker.GetMock<IElectionCastIdempotencyCacheService>().Object);

        var response = await sut.GetElectionVotingViewAsync(
            election.ElectionId,
            "voter-address",
            "cached-election-key");

        response.Success.Should().BeTrue();
        response.PersonalParticipationStatus.Should().Be(ElectionParticipationStatusProto.ParticipationCountedAsVoted);
        response.SubmissionStatus.Should().Be(ElectionVotingSubmissionStatusProto.VotingSubmissionStatusAlreadyUsed);
        response.HasAcceptedAt.Should().BeTrue();

        mocker.GetMock<IElectionsRepository>()
            .Verify(x => x.GetCastIdempotencyRecordAsync(It.IsAny<ElectionId>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task VerifyElectionReceiptAsync_WithMatchingAcceptedCheckoff_ReturnsConfirmedCheckoffState()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            "1001",
            ElectionParticipationStatus.CountedAsVoted,
            openedAt.AddMinutes(4));
        var checkoffConsumption = ElectionModelFactory.CreateCheckoffConsumptionRecord(
            election.ElectionId,
            "1001",
            openedAt.AddMinutes(4));

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
                .ReturnsAsync(rosterEntry);
            repo.Setup(x => x.GetParticipationRecordAsync(election.ElectionId, "1001"))
                .ReturnsAsync(participation);
            repo.Setup(x => x.GetCheckoffConsumptionAsync(election.ElectionId, "1001"))
                .ReturnsAsync(checkoffConsumption);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.VerifyElectionReceiptAsync(
            election.ElectionId,
            "voter-address",
            BuildExpectedReceiptId(checkoffConsumption),
            checkoffConsumption.Id.ToString(),
            BuildExpectedReceiptProof(checkoffConsumption));

        response.Success.Should().BeTrue();
        response.ElectionId.Should().Be(election.ElectionId.ToString());
        response.LifecycleState.Should().Be(ElectionLifecycleStateProto.Open);
        response.HasAcceptedCheckoff.Should().BeTrue();
        response.ReceiptMatchesAcceptedCheckoff.Should().BeTrue();
        response.ParticipationCountedAsVoted.Should().BeTrue();
        response.TallyVerificationAvailable.Should().BeFalse();
        response.VerifiedReceiptId.Should().Be(BuildExpectedReceiptId(checkoffConsumption));
        response.VerifiedAcceptanceId.Should().Be(checkoffConsumption.Id.ToString());
        response.VerifiedServerProof.Should().Be(BuildExpectedReceiptProof(checkoffConsumption));
    }

    [Fact]
    public async Task VerifyElectionReceiptAsync_WithMismatchedReceipt_ReturnsMismatchWithoutFailingRequest()
    {
        var mocker = new AutoMocker();
        var openedAt = DateTime.UtcNow.AddMinutes(-15);
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));
        var participation = ElectionModelFactory.CreateParticipationRecord(
            election.ElectionId,
            "1001",
            ElectionParticipationStatus.CountedAsVoted,
            openedAt.AddMinutes(4));
        var checkoffConsumption = ElectionModelFactory.CreateCheckoffConsumptionRecord(
            election.ElectionId,
            "1001",
            openedAt.AddMinutes(4));

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
                .ReturnsAsync(rosterEntry);
            repo.Setup(x => x.GetParticipationRecordAsync(election.ElectionId, "1001"))
                .ReturnsAsync(participation);
            repo.Setup(x => x.GetCheckoffConsumptionAsync(election.ElectionId, "1001"))
                .ReturnsAsync(checkoffConsumption);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.VerifyElectionReceiptAsync(
            election.ElectionId,
            "voter-address",
            "receipt-does-not-match",
            checkoffConsumption.Id.ToString(),
            BuildExpectedReceiptProof(checkoffConsumption));

        response.Success.Should().BeTrue();
        response.HasAcceptedCheckoff.Should().BeTrue();
        response.ReceiptMatchesAcceptedCheckoff.Should().BeFalse();
        response.ParticipationCountedAsVoted.Should().BeTrue();
    }

    [Fact]
    public async Task GetElectionAsync_WithDevProfilesDisabled_FiltersDevOnlyCeremonyProfiles()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection();
        var prodProfile = ElectionModelFactory.CreateCeremonyProfile(
            "prod-1of1-v1",
            "Production 1 of 1",
            "Production profile",
            "provider-a",
            "v1",
            trusteeCount: 1,
            requiredApprovalCount: 1,
            devOnly: false);
        var devProfile = ElectionModelFactory.CreateCeremonyProfile(
            "dev-1of1-v1",
            "Dev 1 of 1",
            "Dev profile",
            "provider-a",
            "v1",
            trusteeCount: 1,
            requiredApprovalCount: 1,
            devOnly: true);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetCeremonyProfilesAsync()).ReturnsAsync([prodProfile, devProfile]);
        });

        var sut = CreateQueryService(mocker, new ElectionCeremonyOptions(EnableDevCeremonyProfiles: false));

        var response = await sut.GetElectionAsync(election.ElectionId);

        response.Success.Should().BeTrue();
        response.CeremonyProfiles.Should().ContainSingle();
        response.CeremonyProfiles[0].ProfileId.Should().Be(prodProfile.ProfileId);
    }

    [Fact]
    public async Task GetElectionResultViewAsync_WithParticipantActor_ReturnsUnofficialAndOfficialArtifacts()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-2),
            ClosedProgressStatus = ElectionClosedProgressStatus.None,
            OfficialResultVisibilityPolicy = OfficialResultVisibilityPolicy.ParticipantEncryptedOnly,
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "5001",
                ElectionRosterContactType.Email,
                "participant@example.com",
                ElectionVotingRightStatus.Active)
            .LinkToActor("participant-address", DateTime.UtcNow);
        var denominatorEvidence = new SharedResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [1, 2, 3]);
        var unofficial = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            [
                new SharedResultOptionCount("alice", "Alice", null, 1, 1, 7),
                new SharedResultOptionCount("bob", "Bob", null, 2, 2, 5),
            ],
            blankCount: 1,
            totalVotedCount: 13,
            eligibleToVoteCount: 20,
            didNotVoteCount: 7,
            denominatorEvidence,
            "owner-address",
            encryptedPayload: "enc::unofficial");
        var official = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            unofficial.NamedOptionResults,
            unofficial.BlankCount,
            unofficial.TotalVotedCount,
            unofficial.EligibleToVoteCount,
            unofficial.DidNotVoteCount,
            denominatorEvidence,
            "owner-address",
            sourceResultArtifactId: unofficial.Id,
            encryptedPayload: "enc::official");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "participant-address")).ReturnsAsync(rosterEntry);
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Unofficial)).ReturnsAsync(unofficial);
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Official)).ReturnsAsync(official);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionResultViewAsync(election.ElectionId, "participant-address");

        response.Success.Should().BeTrue();
        response.CanViewParticipantEncryptedResults.Should().BeTrue();
        response.CanViewReportPackage.Should().BeFalse();
        response.UnofficialResult.Should().NotBeNull();
        response.OfficialResult.Should().NotBeNull();
        response.UnofficialResult.EncryptedPayload.Should().Be("enc::unofficial");
        response.OfficialResult.EncryptedPayload.Should().Be("enc::official");
    }

    [Fact]
    public async Task GetElectionResultViewAsync_WithClosedElection_TriggersClosedResultRepairBeforeReading()
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            ClosedAt = DateTime.UtcNow.AddMinutes(-2),
            VoteAcceptanceLockedAt = DateTime.UtcNow.AddMinutes(-2),
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "5001",
                ElectionRosterContactType.Email,
                "participant@example.com",
                ElectionVotingRightStatus.Active)
            .LinkToActor("participant-address", DateTime.UtcNow);
        var denominatorEvidence = new SharedResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [1, 2, 3]);
        var unofficial = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            [
                new SharedResultOptionCount("alice", "Alice", null, 1, 1, 1),
                new SharedResultOptionCount("bob", "Bob", null, 2, 2, 0),
            ],
            blankCount: 0,
            totalVotedCount: 1,
            eligibleToVoteCount: 2,
            didNotVoteCount: 1,
            denominatorEvidence,
            "owner-address",
            encryptedPayload: "enc::repaired");
        var publicationService = new Mock<IElectionBallotPublicationService>(MockBehavior.Strict);
        publicationService
            .Setup(x => x.RepairClosedElectionResultsAsync(election.ElectionId))
            .Returns(Task.CompletedTask);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
            repo.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "participant-address")).ReturnsAsync(rosterEntry);
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Unofficial)).ReturnsAsync(unofficial);
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Official))
                .ReturnsAsync((ElectionResultArtifactRecord?)null);
        });

        var sut = CreateQueryService(
            mocker,
            electionBallotPublicationService: publicationService.Object);

        var response = await sut.GetElectionResultViewAsync(election.ElectionId, "participant-address");

        response.Success.Should().BeTrue();
        response.UnofficialResult.Should().NotBeNull();
        response.UnofficialResult.EncryptedPayload.Should().Be("enc::repaired");
        publicationService.Verify(x => x.RepairClosedElectionResultsAsync(election.ElectionId), Times.Once);
    }

    [Fact]
    public async Task GetElectionAsync_WithClosedElectionMissingUnofficialResult_RepairsBeforeReturningDetail()
    {
        var mocker = new AutoMocker();
        var unrepairedElection = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            OpenedAt = DateTime.UtcNow.AddMinutes(-15),
            ClosedAt = DateTime.UtcNow.AddMinutes(-1),
            VoteAcceptanceLockedAt = DateTime.UtcNow.AddMinutes(-1),
            OpenArtifactId = Guid.NewGuid(),
            CloseArtifactId = Guid.NewGuid(),
            ClosedProgressStatus = ElectionClosedProgressStatus.TallyCalculationInProgress,
        };
        var repairedElection = unrepairedElection with
        {
            UnofficialResultArtifactId = Guid.NewGuid(),
        };
        var publicationService = new Mock<IElectionBallotPublicationService>(MockBehavior.Strict);
        publicationService
            .Setup(x => x.RepairClosedElectionResultsAsync(unrepairedElection.ElectionId))
            .Returns(Task.CompletedTask);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.SetupSequence(x => x.GetElectionAsync(unrepairedElection.ElectionId))
                .ReturnsAsync(unrepairedElection)
                .ReturnsAsync(repairedElection);
        });

        var sut = CreateQueryService(
            mocker,
            electionBallotPublicationService: publicationService.Object);

        var response = await sut.GetElectionAsync(unrepairedElection.ElectionId);

        response.Success.Should().BeTrue();
        response.Election.Should().NotBeNull();
        response.Election!.UnofficialResultArtifactId.Should().Be(repairedElection.UnofficialResultArtifactId!.Value.ToString());
        publicationService.Verify(x => x.RepairClosedElectionResultsAsync(unrepairedElection.ElectionId), Times.Once);
    }

    [Fact]
    public async Task GetElectionResultViewAsync_WithNonParticipantActor_HidesUnofficialButReturnsPublicOfficial()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-2),
            ClosedProgressStatus = ElectionClosedProgressStatus.None,
            OfficialResultVisibilityPolicy = OfficialResultVisibilityPolicy.PublicPlaintext,
        };
        var denominatorEvidence = new SharedResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [1, 2, 3]);
        var unofficial = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            [
                new SharedResultOptionCount("alice", "Alice", null, 1, 1, 7),
                new SharedResultOptionCount("bob", "Bob", null, 2, 2, 5),
            ],
            blankCount: 1,
            totalVotedCount: 13,
            eligibleToVoteCount: 20,
            didNotVoteCount: 7,
            denominatorEvidence,
            "owner-address",
            encryptedPayload: "enc::unofficial");
        var official = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.PublicPlaintext,
            election.Title,
            unofficial.NamedOptionResults,
            unofficial.BlankCount,
            unofficial.TotalVotedCount,
            unofficial.EligibleToVoteCount,
            unofficial.DidNotVoteCount,
            denominatorEvidence,
            "owner-address",
            sourceResultArtifactId: unofficial.Id,
            publicPayload: "{\"title\":\"Board Election\"}");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Unofficial)).ReturnsAsync(unofficial);
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Official)).ReturnsAsync(official);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionResultViewAsync(election.ElectionId, "outsider-address");

        response.Success.Should().BeTrue();
        response.CanViewParticipantEncryptedResults.Should().BeFalse();
        response.CanViewReportPackage.Should().BeFalse();
        response.UnofficialResult.Should().BeNull();
        response.OfficialResult.Should().NotBeNull();
        response.OfficialResult.PublicPayload.Should().Contain("Board Election");
    }

    [Fact]
    public async Task GetElectionResultViewAsync_WithOwnerAndFailedPackageAttempt_ReturnsRetryablePackageSummary()
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection() with
        {
            LifecycleState = ElectionLifecycleState.Closed,
            ClosedAt = DateTime.UtcNow.AddMinutes(-5),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-4),
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
        };
        var reportPackage = ElectionModelFactory.CreateFailedReportPackageAttempt(
            election.ElectionId,
            attemptNumber: 1,
            tallyReadyArtifactId: election.TallyReadyArtifactId!.Value,
            unofficialResultArtifactId: election.UnofficialResultArtifactId!.Value,
            frozenEvidenceHash: [1, 2, 3],
            frozenEvidenceFingerprint: "fingerprint-1",
            attemptedByPublicAddress: "owner-address",
            failureCode: "CONSISTENCY_MISMATCH",
            failureReason: "Human and machine artifacts diverged.",
            closeBoundaryArtifactId: Guid.NewGuid(),
            closeEligibilitySnapshotId: Guid.NewGuid(),
            attemptedAt: DateTime.UtcNow.AddMinutes(-1));

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetLatestReportPackageAsync(election.ElectionId)).ReturnsAsync(reportPackage);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionResultViewAsync(election.ElectionId, "owner-address");

        response.Success.Should().BeTrue();
        response.CanViewReportPackage.Should().BeTrue();
        response.CanRetryFailedPackageFinalization.Should().BeTrue();
        response.LatestReportPackage.Should().NotBeNull();
        response.LatestReportPackage.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageGenerationFailed);
        response.LatestReportPackage.FailureCode.Should().Be("CONSISTENCY_MISMATCH");
        response.VisibleReportArtifacts.Should().BeEmpty();
    }

    [Fact]
    public async Task GetElectionResultViewAsync_WithTrusteeRole_FiltersRosterOnlyPackageArtifacts()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection() with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-1),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-5),
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
            OfficialResultArtifactId = Guid.NewGuid(),
            FinalizeArtifactId = Guid.NewGuid(),
        };
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                election.ElectionId,
                trusteeUserAddress: "trustee-a",
                trusteeDisplayName: "Alice",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: election.CurrentDraftRevision)
            .Accept(
                respondedAt: DateTime.UtcNow,
                resolvedAtDraftRevision: election.CurrentDraftRevision,
                lifecycleState: ElectionLifecycleState.Draft);
        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
            election.ElectionId,
            attemptNumber: 1,
            tallyReadyArtifactId: election.TallyReadyArtifactId!.Value,
            unofficialResultArtifactId: election.UnofficialResultArtifactId!.Value,
            officialResultArtifactId: election.OfficialResultArtifactId!.Value,
            finalizeArtifactId: election.FinalizeArtifactId!.Value,
            frozenEvidenceHash: [1, 2, 3],
            frozenEvidenceFingerprint: "fingerprint-2",
            packageHash: [4, 5, 6],
            artifactCount: 2,
            attemptedByPublicAddress: "owner-address",
            attemptedAt: DateTime.UtcNow.AddMinutes(-1),
            sealedAt: DateTime.UtcNow);
        var trusteeVisibleArtifact = ElectionModelFactory.CreateReportArtifact(
            reportPackage.Id,
            election.ElectionId,
            ElectionReportArtifactKind.HumanManifest,
            ElectionReportArtifactFormat.Markdown,
            ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
            1,
            "Final manifest",
            "final-manifest.md",
            "text/markdown",
            [9, 9, 9],
            "# Manifest");
        var ownerOnlyArtifact = ElectionModelFactory.CreateReportArtifact(
            reportPackage.Id,
            election.ElectionId,
            ElectionReportArtifactKind.HumanNamedParticipationRoster,
            ElectionReportArtifactFormat.Markdown,
            ElectionReportArtifactAccessScope.OwnerAuditorOnly,
            2,
            "Named participation roster",
            "named-participation-roster.md",
            "text/markdown",
            [8, 8, 8],
            "# Roster");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync([acceptedInvitation]);
            repo.Setup(x => x.GetLatestReportPackageAsync(election.ElectionId)).ReturnsAsync(reportPackage);
            repo.Setup(x => x.GetReportArtifactsAsync(reportPackage.Id)).ReturnsAsync([trusteeVisibleArtifact, ownerOnlyArtifact]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionResultViewAsync(election.ElectionId, "trustee-a");

        response.Success.Should().BeTrue();
        response.CanViewReportPackage.Should().BeTrue();
        response.CanRetryFailedPackageFinalization.Should().BeFalse();
        response.LatestReportPackage.Should().NotBeNull();
        response.LatestReportPackage.Status.Should().Be(ElectionReportPackageStatusProto.ReportPackageSealed);
        response.VisibleReportArtifacts.Should().ContainSingle();
        response.VisibleReportArtifacts[0].ArtifactKind.Should().Be(ElectionReportArtifactKindProto.ReportArtifactHumanManifest);
    }

    [Fact]
    public async Task GetElectionResultViewAsync_WithDesignatedAuditorRole_ReturnsParticipantResultsAndRestrictedArtifacts()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection() with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            FinalizedAt = DateTime.UtcNow.AddMinutes(-1),
            TallyReadyAt = DateTime.UtcNow.AddMinutes(-5),
            TallyReadyArtifactId = Guid.NewGuid(),
            UnofficialResultArtifactId = Guid.NewGuid(),
            OfficialResultArtifactId = Guid.NewGuid(),
            FinalizeArtifactId = Guid.NewGuid(),
            OfficialResultVisibilityPolicy = OfficialResultVisibilityPolicy.ParticipantEncryptedOnly,
        };
        var denominatorEvidence = new SharedResultDenominatorEvidence(
            ElectionEligibilitySnapshotType.Close,
            Guid.NewGuid(),
            Guid.NewGuid(),
            [1, 2, 3]);
        var unofficial = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Unofficial,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            [
                new SharedResultOptionCount("alice", "Alice", null, 1, 1, 7),
                new SharedResultOptionCount("bob", "Bob", null, 2, 2, 5),
            ],
            blankCount: 1,
            totalVotedCount: 13,
            eligibleToVoteCount: 20,
            didNotVoteCount: 7,
            denominatorEvidence,
            "owner-address",
            encryptedPayload: "enc::unofficial");
        var official = ElectionModelFactory.CreateResultArtifact(
            election.ElectionId,
            ElectionResultArtifactKind.Official,
            ElectionResultArtifactVisibility.ParticipantEncrypted,
            election.Title,
            unofficial.NamedOptionResults,
            unofficial.BlankCount,
            unofficial.TotalVotedCount,
            unofficial.EligibleToVoteCount,
            unofficial.DidNotVoteCount,
            denominatorEvidence,
            "owner-address",
            sourceResultArtifactId: unofficial.Id,
            encryptedPayload: "enc::official");
        var reportPackage = ElectionModelFactory.CreateSealedReportPackage(
            election.ElectionId,
            attemptNumber: 1,
            tallyReadyArtifactId: election.TallyReadyArtifactId!.Value,
            unofficialResultArtifactId: election.UnofficialResultArtifactId!.Value,
            officialResultArtifactId: election.OfficialResultArtifactId!.Value,
            finalizeArtifactId: election.FinalizeArtifactId!.Value,
            frozenEvidenceHash: [1, 2, 3],
            frozenEvidenceFingerprint: "fingerprint-2",
            packageHash: [4, 5, 6],
            artifactCount: 2,
            attemptedByPublicAddress: "owner-address",
            attemptedAt: DateTime.UtcNow.AddMinutes(-1),
            sealedAt: DateTime.UtcNow);
        var trusteeVisibleArtifact = ElectionModelFactory.CreateReportArtifact(
            reportPackage.Id,
            election.ElectionId,
            ElectionReportArtifactKind.HumanManifest,
            ElectionReportArtifactFormat.Markdown,
            ElectionReportArtifactAccessScope.OwnerAuditorTrustee,
            1,
            "Final manifest",
            "final-manifest.md",
            "text/markdown",
            [9, 9, 9],
            "# Manifest");
        var ownerAuditorArtifact = ElectionModelFactory.CreateReportArtifact(
            reportPackage.Id,
            election.ElectionId,
            ElectionReportArtifactKind.HumanNamedParticipationRoster,
            ElectionReportArtifactFormat.Markdown,
            ElectionReportArtifactAccessScope.OwnerAuditorOnly,
            2,
            "Named participation roster",
            "named-participation-roster.md",
            "text/markdown",
            [8, 8, 8],
            "# Roster");
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            actorPublicAddress: "auditor-address",
            grantedByPublicAddress: "owner-address");

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, "auditor-address"))
                .ReturnsAsync(auditorGrant);
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Unofficial))
                .ReturnsAsync(unofficial);
            repo.Setup(x => x.GetResultArtifactAsync(election.ElectionId, ElectionResultArtifactKind.Official))
                .ReturnsAsync(official);
            repo.Setup(x => x.GetLatestReportPackageAsync(election.ElectionId)).ReturnsAsync(reportPackage);
            repo.Setup(x => x.GetReportArtifactsAsync(reportPackage.Id)).ReturnsAsync([trusteeVisibleArtifact, ownerAuditorArtifact]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionResultViewAsync(election.ElectionId, "auditor-address");

        response.Success.Should().BeTrue();
        response.CanViewParticipantEncryptedResults.Should().BeTrue();
        response.CanViewReportPackage.Should().BeTrue();
        response.UnofficialResult.Should().NotBeNull();
        response.OfficialResult.Should().NotBeNull();
        response.VisibleReportArtifacts.Should().HaveCount(2);
        response.VisibleReportArtifacts.Select(x => x.ArtifactKind).Should().Contain(
            ElectionReportArtifactKindProto.ReportArtifactHumanNamedParticipationRoster);
    }

    [Fact]
    public async Task GetElectionReportAccessGrantsAsync_WithOwnerRole_ReturnsSortedGrantList()
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection();
        var olderGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            actorPublicAddress: "auditor-b",
            grantedByPublicAddress: "owner-address",
            grantedAt: DateTime.UtcNow.AddMinutes(-5));
        var newerGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            actorPublicAddress: "auditor-a",
            grantedByPublicAddress: "owner-address",
            grantedAt: DateTime.UtcNow.AddMinutes(-1));

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetReportAccessGrantsAsync(election.ElectionId)).ReturnsAsync([olderGrant, newerGrant]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionReportAccessGrantsAsync(election.ElectionId, "owner-address");

        response.Success.Should().BeTrue();
        response.CanManageGrants.Should().BeTrue();
        response.DeniedReason.Should().BeEmpty();
        response.Grants.Select(x => x.ActorPublicAddress).Should().Equal("auditor-a", "auditor-b");
    }

    [Fact]
    public async Task GetElectionReportAccessGrantsAsync_WithNonOwnerRole_ReturnsDeniedResponse()
    {
        var mocker = new AutoMocker();
        var election = CreateAdminElection();

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionReportAccessGrantsAsync(election.ElectionId, "auditor-address");

        response.Success.Should().BeTrue();
        response.CanManageGrants.Should().BeFalse();
        response.DeniedReason.Should().Contain("Only the election owner");
        response.Grants.Should().BeEmpty();
    }

    [Fact]
    public async Task GetElectionCeremonyActionViewAsync_WithOwnerRole_ReturnsStartAndRestartAvailability()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection();
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                election.ElectionId,
                trusteeUserAddress: "trustee-a",
                trusteeDisplayName: "Alice",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: election.CurrentDraftRevision)
            .Accept(
                respondedAt: DateTime.UtcNow,
                resolvedAtDraftRevision: election.CurrentDraftRevision,
                lifecycleState: election.LifecycleState);
        var profile = ElectionModelFactory.CreateCeremonyProfile(
            "prod-1of1-v1",
            "Production 1 of 1",
            "Production profile",
            "provider-a",
            "v1",
            trusteeCount: 1,
            requiredApprovalCount: 1,
            devOnly: false);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync([acceptedInvitation]);
            repo.Setup(x => x.GetCeremonyProfilesAsync()).ReturnsAsync([profile]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionCeremonyActionViewAsync(election.ElectionId, "owner-address");

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionCeremonyActorRoleProto.CeremonyActorOwner);
        response.OwnerActions.Should().HaveCount(2);
        response.OwnerActions.Single(x => x.ActionType == ElectionCeremonyActionTypeProto.CeremonyActionStartVersion).IsAvailable.Should().BeTrue();
        response.OwnerActions.Single(x => x.ActionType == ElectionCeremonyActionTypeProto.CeremonyActionRestartVersion).IsAvailable.Should().BeFalse();
    }

    [Fact]
    public async Task GetElectionCeremonyActionViewAsync_WithTrusteeRole_ReturnsPersonalCeremonyStepsOnly()
    {
        var mocker = new AutoMocker();
        var election = CreateTrusteeElection();
        var acceptedInvitation = ElectionModelFactory.CreateTrusteeInvitation(
                election.ElectionId,
                trusteeUserAddress: "trustee-a",
                trusteeDisplayName: "Alice",
                invitedByPublicAddress: "owner-address",
                sentAtDraftRevision: election.CurrentDraftRevision)
            .Accept(
                respondedAt: DateTime.UtcNow,
                resolvedAtDraftRevision: election.CurrentDraftRevision,
                lifecycleState: election.LifecycleState);
        var profile = ElectionModelFactory.CreateCeremonyProfile(
            "prod-1of1-v1",
            "Production 1 of 1",
            "Production profile",
            "provider-a",
            "v1",
            trusteeCount: 1,
            requiredApprovalCount: 1,
            devOnly: false);
        var version = ElectionModelFactory.CreateCeremonyVersion(
            election.ElectionId,
            versionNumber: 1,
            profileId: profile.ProfileId,
            requiredApprovalCount: 1,
            boundTrustees:
            [
                new SharedTrusteeReference("trustee-a", "Alice"),
            ],
            startedByPublicAddress: "owner-address");
        var trusteeState = ElectionModelFactory.CreateCeremonyTrusteeState(
                election.ElectionId,
                version.Id,
                "trustee-a",
                "Alice",
                ElectionTrusteeCeremonyState.AcceptedTrustee)
            .PublishTransportKey("transport-a", DateTime.UtcNow)
            .MarkJoined(DateTime.UtcNow)
            .RecordSelfTestSuccess(DateTime.UtcNow)
            .RecordMaterialSubmitted(DateTime.UtcNow)
            .RecordValidationFailure("Wrong version payload.", DateTime.UtcNow);

        ConfigureReadOnlyRepository(mocker, repo =>
        {
            repo.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
            repo.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId)).ReturnsAsync([acceptedInvitation]);
            repo.Setup(x => x.GetCeremonyProfilesAsync()).ReturnsAsync([profile]);
            repo.Setup(x => x.GetActiveCeremonyVersionAsync(election.ElectionId)).ReturnsAsync(version);
            repo.Setup(x => x.GetCeremonyTrusteeStatesAsync(version.Id)).ReturnsAsync([trusteeState]);
            repo.Setup(x => x.GetCeremonyShareCustodyRecordAsync(version.Id, "trustee-a"))
                .ReturnsAsync((ElectionCeremonyShareCustodyRecord?)null);
            repo.Setup(x => x.GetCeremonyMessageEnvelopesForRecipientAsync(version.Id, "trustee-a"))
                .ReturnsAsync(
                [
                    ElectionModelFactory.CreateCeremonyMessageEnvelope(
                        election.ElectionId,
                        version.Id,
                        version.VersionNumber,
                        version.ProfileId,
                        senderTrusteeUserAddress: "trustee-b",
                        recipientTrusteeUserAddress: "trustee-a",
                        messageType: "share-package",
                        payloadVersion: "v1",
                        encryptedPayload: [1, 2, 3],
                        payloadFingerprint: "payload-fingerprint"),
                ]);
        });

        var sut = CreateQueryService(mocker);

        var response = await sut.GetElectionCeremonyActionViewAsync(election.ElectionId, "trustee-a");

        response.Success.Should().BeTrue();
        response.ActorRole.Should().Be(ElectionCeremonyActorRoleProto.CeremonyActorTrustee);
        response.OwnerActions.Should().BeEmpty();
        response.TrusteeActions.Should().HaveCount(6);
        response.PendingIncomingMessageCount.Should().Be(1);
        response.SelfTrusteeState.Should().NotBeNull();
        response.SelfTrusteeState!.TrusteeUserAddress.Should().Be("trustee-a");
        response.TrusteeActions.Single(x => x.ActionType == ElectionCeremonyActionTypeProto.CeremonyActionPublishTransportKey).IsCompleted.Should().BeTrue();
        response.TrusteeActions.Single(x => x.ActionType == ElectionCeremonyActionTypeProto.CeremonyActionRunSelfTest).IsAvailable.Should().BeTrue();
    }

    private static void ConfigureReadOnlyRepository(AutoMocker mocker, Action<Mock<IElectionsRepository>> configureRepository)
    {
        var unitOfWork = mocker.GetMock<IReadOnlyUnitOfWork<ElectionsDbContext>>();
        var writableUnitOfWork = mocker.GetMock<IWritableUnitOfWork<ElectionsDbContext>>();
        var repository = mocker.GetMock<IElectionsRepository>();

        mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(unitOfWork.Object);
        mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>()
            .Setup(x => x.CreateWritable(It.IsAny<IsolationLevel>()))
            .Returns(writableUnitOfWork.Object);
        unitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);
        writableUnitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);
        writableUnitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);

        repository.Setup(x => x.GetElectionsByOwnerAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionRecord>());
        repository.Setup(x => x.GetElectionsByIdsAsync(It.IsAny<IReadOnlyCollection<ElectionId>>()))
            .ReturnsAsync(Array.Empty<ElectionRecord>());
        repository.Setup(x => x.GetWarningAcknowledgementsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionWarningAcknowledgementRecord>());
        repository.Setup(x => x.GetTrusteeInvitationsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        repository.Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        repository.Setup(x => x.GetBoundaryArtifactsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionBoundaryArtifactRecord>());
        repository.Setup(x => x.UpdateBoundaryArtifactAsync(It.IsAny<ElectionBoundaryArtifactRecord>()))
            .Returns(Task.CompletedTask);
        repository.Setup(x => x.GetGovernedProposalsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionGovernedProposalRecord>());
        repository.Setup(x => x.GetPendingGovernedProposalAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionGovernedProposalRecord?)null);
        repository.Setup(x => x.GetGovernedProposalApprovalsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<ElectionGovernedProposalApprovalRecord>());
        repository.Setup(x => x.GetCeremonyProfilesAsync())
            .ReturnsAsync(Array.Empty<ElectionCeremonyProfileRecord>());
        repository.Setup(x => x.GetCeremonyVersionsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionCeremonyVersionRecord>());
        repository.Setup(x => x.GetActiveCeremonyVersionAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionCeremonyVersionRecord?)null);
        repository.Setup(x => x.GetCeremonyTranscriptEventsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<ElectionCeremonyTranscriptEventRecord>());
        repository.Setup(x => x.GetCeremonyTrusteeStatesAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<ElectionCeremonyTrusteeStateRecord>());
        repository.Setup(x => x.GetFinalizationSessionsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionFinalizationSessionRecord>());
        repository.Setup(x => x.GetFinalizationSharesAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<ElectionFinalizationShareRecord>());
        repository.Setup(x => x.GetFinalizationReleaseEvidenceRecordsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionFinalizationReleaseEvidenceRecord>());
        repository.Setup(x => x.GetResultArtifactsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionResultArtifactRecord>());
        repository.Setup(x => x.GetResultArtifactAsync(It.IsAny<Guid>()))
            .ReturnsAsync((ElectionResultArtifactRecord?)null);
        repository.Setup(x => x.GetResultArtifactAsync(It.IsAny<ElectionId>(), It.IsAny<ElectionResultArtifactKind>()))
            .ReturnsAsync((ElectionResultArtifactRecord?)null);
        repository.Setup(x => x.GetLatestReportPackageAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync((ElectionReportPackageRecord?)null);
        repository.Setup(x => x.GetReportArtifactsAsync(It.IsAny<Guid>()))
            .ReturnsAsync(Array.Empty<ElectionReportArtifactRecord>());
        repository.Setup(x => x.GetReportAccessGrantsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionReportAccessGrantRecord>());
        repository.Setup(x => x.GetReportAccessGrantsByActorAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionReportAccessGrantRecord>());
        repository.Setup(x => x.GetReportAccessGrantAsync(It.IsAny<ElectionId>(), It.IsAny<string>()))
            .ReturnsAsync((ElectionReportAccessGrantRecord?)null);
        repository.Setup(x => x.GetRosterEntriesAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionRosterEntryRecord>());
        repository.Setup(x => x.GetRosterEntriesByLinkedActorAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionRosterEntryRecord>());
        repository.Setup(x => x.GetParticipationRecordsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionParticipationRecord>());
        repository.Setup(x => x.GetParticipationRecordAsync(It.IsAny<ElectionId>(), It.IsAny<string>()))
            .ReturnsAsync((ElectionParticipationRecord?)null);
        repository.Setup(x => x.GetEligibilityActivationEventsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionEligibilityActivationEventRecord>());
        repository.Setup(x => x.GetEligibilitySnapshotsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionEligibilitySnapshotRecord>());
        repository.Setup(x => x.GetRosterEntryByLinkedActorAsync(It.IsAny<ElectionId>(), It.IsAny<string>()))
            .ReturnsAsync((ElectionRosterEntryRecord?)null);
        repository.Setup(x => x.GetCeremonyShareCustodyRecordAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((ElectionCeremonyShareCustodyRecord?)null);
        repository.Setup(x => x.GetCeremonyMessageEnvelopesForRecipientAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionCeremonyMessageEnvelopeRecord>());

        configureRepository(repository);
    }

    private static ElectionQueryApplicationService CreateQueryService(
        AutoMocker mocker,
        ElectionCeremonyOptions? options = null,
        IMemPoolService? memPoolService = null,
        IElectionEnvelopeCryptoService? electionEnvelopeCryptoService = null,
        IElectionCastIdempotencyCacheService? castIdempotencyCacheService = null,
        IElectionBallotPublicationService? electionBallotPublicationService = null) =>
        new(
            mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>().Object,
            options ?? new ElectionCeremonyOptions(),
            memPoolService,
            electionEnvelopeCryptoService,
            castIdempotencyCacheService,
            electionBallotPublicationService);

    private static ElectionRecord CreateAdminElection(
        string title = "Board Election",
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: title,
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
            acknowledgedWarningCodes: acknowledgedWarningCodes);

    private static ElectionRecord CreateTrusteeElection(
        string title = "Board Election",
        IReadOnlyList<ElectionWarningCode>? acknowledgedWarningCodes = null) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: title,
            shortDescription: "Annual board vote",
            ownerPublicAddress: "owner-address",
            externalReferenceCode: "ORG-2026-01",
            electionClass: ElectionClass.OrganizationalRemoteVoting,
            bindingStatus: ElectionBindingStatus.Binding,
            governanceMode: ElectionGovernanceMode.TrusteeThreshold,
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
            reviewWindowPolicy: ReviewWindowPolicy.GovernedReviewWindowReserved,
            ownerOptions:
            [
                new ElectionOptionDefinition("alice", "Alice", null, 1, IsBlankOption: false),
                new ElectionOptionDefinition("bob", "Bob", null, 2, IsBlankOption: false),
            ],
            acknowledgedWarningCodes: acknowledgedWarningCodes,
            requiredApprovalCount: 1);

    private static string ComputeScopedHash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    private static string BuildExpectedReceiptId(ElectionCheckoffConsumptionRecord checkoffConsumption)
    {
        var receiptSeed = string.Join(
            "|",
            checkoffConsumption.ElectionId,
            checkoffConsumption.Id,
            checkoffConsumption.ConsumedAt.ToUniversalTime().ToString("O"),
            checkoffConsumption.ParticipationStatus);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(receiptSeed));
        return $"rcpt-{Convert.ToHexString(hash)[..24].ToLowerInvariant()}";
    }

    private static string BuildExpectedReceiptProof(ElectionCheckoffConsumptionRecord checkoffConsumption)
    {
        var proofSeed = string.Join(
            "|",
            BuildExpectedReceiptId(checkoffConsumption),
            checkoffConsumption.ElectionId,
            checkoffConsumption.Id,
            checkoffConsumption.ConsumedAt.ToUniversalTime().ToString("O"),
            checkoffConsumption.ParticipationStatus);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(proofSeed))).ToLowerInvariant();
    }
}
