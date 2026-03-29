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
using System.Security.Cryptography;
using System.Text;
using Xunit;

namespace HushServerNode.Tests.Elections;

using SharedTrusteeReference = HushShared.Elections.Model.ElectionTrusteeReference;

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
    public async Task GetElectionEnvelopeAccessAsync_WithStoredAccess_ReturnsWrappedElectionKey()
    {
        var mocker = new AutoMocker();
        var electionId = ElectionId.NewElectionId;
        var accessRecord = new ElectionEnvelopeAccessRecord(
            electionId,
            "trustee-address",
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
        var repository = mocker.GetMock<IElectionsRepository>();

        mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(unitOfWork.Object);
        unitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);

        repository.Setup(x => x.GetWarningAcknowledgementsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionWarningAcknowledgementRecord>());
        repository.Setup(x => x.GetTrusteeInvitationsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        repository.Setup(x => x.GetBoundaryArtifactsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionBoundaryArtifactRecord>());
        repository.Setup(x => x.GetGovernedProposalsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionGovernedProposalRecord>());
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
        repository.Setup(x => x.GetRosterEntriesAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionRosterEntryRecord>());
        repository.Setup(x => x.GetParticipationRecordsAsync(It.IsAny<ElectionId>()))
            .ReturnsAsync(Array.Empty<ElectionParticipationRecord>());
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
        IElectionCastIdempotencyCacheService? castIdempotencyCacheService = null) =>
        new(
            mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>().Object,
            options ?? new ElectionCeremonyOptions(),
            memPoolService,
            electionEnvelopeCryptoService,
            castIdempotencyCacheService);

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
}
