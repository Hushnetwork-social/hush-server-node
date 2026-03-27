using FluentAssertions;
using HushNetwork.proto;
using HushNode.Elections;
using HushNode.Elections.gRPC;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
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
        repository.Setup(x => x.GetCeremonyShareCustodyRecordAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync((ElectionCeremonyShareCustodyRecord?)null);
        repository.Setup(x => x.GetCeremonyMessageEnvelopesForRecipientAsync(It.IsAny<Guid>(), It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionCeremonyMessageEnvelopeRecord>());

        configureRepository(repository);
    }

    private static ElectionQueryApplicationService CreateQueryService(
        AutoMocker mocker,
        ElectionCeremonyOptions? options = null) =>
        options is null
            ? new ElectionQueryApplicationService(
                mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>().Object)
            : new ElectionQueryApplicationService(
                mocker.GetMock<IUnitOfWorkProvider<ElectionsDbContext>>().Object,
                options);

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
}
