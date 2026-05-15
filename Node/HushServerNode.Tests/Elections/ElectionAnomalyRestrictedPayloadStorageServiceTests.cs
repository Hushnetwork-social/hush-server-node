using FluentAssertions;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Moq;
using Olimpo.EntityFramework.Persistency;
using System.Security.Cryptography;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyRestrictedPayloadStorageServiceTests
{
    [Fact]
    public async Task StageAsync_WithSubmitterRequestedEvidenceForOpenClarification_SavesPendingPayload()
    {
        var now = DateTime.UtcNow;
        var actorPublicAddress = "submitter-address";
        var election = CreateElection(now);
        var thread = CreateThread(election, actorPublicAddress, now);
        var encryptedPayload = new byte[] { 1, 2, 3, 4 };
        var contentPayload = new byte[] { 5, 6, 7 };
        ElectionAnomalyRestrictedPayloadRecord? savedPayload = null;
        var (sut, unitOfWork, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsAsync(thread.Id))
                    .ReturnsAsync(Array.Empty<ElectionAnomalyAttachmentManifestRecord>());
                repository
                    .Setup(x => x.SaveAnomalyRestrictedPayloadAsync(It.IsAny<ElectionAnomalyRestrictedPayloadRecord>()))
                    .Callback<ElectionAnomalyRestrictedPayloadRecord>(payload => savedPayload = payload)
                    .Returns(Task.CompletedTask);
            });

        var result = await sut.StageAsync(new ElectionAnomalyRestrictedPayloadStageRequest(
            election.ElectionId,
            thread.Id,
            actorPublicAddress,
            ElectionAnomalyAttachmentKindIds.AuthorityRequestedEvidence,
            encryptedPayload,
            Sha256Ref(encryptedPayload),
            Sha256Ref(contentPayload),
            contentPayload.Length,
            ElectionAnomalyEvidenceMimeTypes.ImagePng,
            thread.OpenClarificationRequestId));

        result.Success.Should().BeTrue();
        savedPayload.Should().NotBeNull();
        savedPayload!.PayloadReference.Should().StartWith(ElectionAnomalyRestrictedPayloadReference.Prefix);
        savedPayload.EncryptedPayload.Should().Equal(encryptedPayload);
        savedPayload.EncryptedPayloadHash.Should().Be(Sha256Ref(encryptedPayload));
        savedPayload.ContentHash.Should().Be(Sha256Ref(contentPayload));
        savedPayload.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Pending);
        savedPayload.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Available);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task StageAsync_WithEncryptedPayloadHashMismatch_RejectsBeforeSaving()
    {
        var now = DateTime.UtcNow;
        var actorPublicAddress = "submitter-address";
        var election = CreateElection(now);
        var thread = CreateThread(election, actorPublicAddress, now);
        var (sut, unitOfWork, repository) = CreateService(election, thread);

        var result = await sut.StageAsync(new ElectionAnomalyRestrictedPayloadStageRequest(
            election.ElectionId,
            thread.Id,
            actorPublicAddress,
            ElectionAnomalyAttachmentKindIds.AuthorityRequestedEvidence,
            [1, 2, 3, 4],
            $"sha256:{new string('f', 64)}",
            Sha256Ref([5, 6, 7]),
            3,
            ElectionAnomalyEvidenceMimeTypes.ImagePng,
            thread.OpenClarificationRequestId));

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentHashInvalid);
        repository.Verify(x => x.SaveAnomalyRestrictedPayloadAsync(It.IsAny<ElectionAnomalyRestrictedPayloadRecord>()), Times.Never);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task RetrieveAsync_WithOwner_ReturnsEncryptedPayload()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread);
        var (sut, _, repository) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
            });

        var result = await sut.RetrieveAsync(new ElectionAnomalyRestrictedPayloadRetrieveRequest(
            election.ElectionId,
            "owner-address",
            payload.PayloadReference));

        result.Success.Should().BeTrue();
        result.PayloadRecord.Should().NotBeNull();
        result.PayloadRecord!.EncryptedPayload.Should().Equal(payload.EncryptedPayload);
        result.PayloadRecord.EncryptedPayloadHash.Should().Be(payload.EncryptedPayloadHash);
        result.PayloadRecord.ContentHash.Should().Be(payload.ContentHash);
        repository.Verify(
            x => x.GetReportAccessGrantAsync(It.IsAny<ElectionId>(), It.IsAny<string>()),
            Times.Never);
    }

    [Fact]
    public async Task RetrieveAsync_WithOriginalSubmitter_ReturnsEncryptedPayload()
    {
        var now = DateTime.UtcNow;
        var actorPublicAddress = "submitter-address";
        var election = CreateElection(now);
        var thread = CreateThread(election, actorPublicAddress, now);
        var payload = CreatePayload(election, thread);
        var (sut, _, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
            });

        var result = await sut.RetrieveAsync(new ElectionAnomalyRestrictedPayloadRetrieveRequest(
            election.ElectionId,
            actorPublicAddress,
            payload.PayloadReference));

        result.Success.Should().BeTrue();
        result.PayloadRecord.Should().NotBeNull();
        result.PayloadRecord!.PayloadReference.Should().Be(payload.PayloadReference);
        result.PayloadRecord.EncryptedPayload.Should().Equal(payload.EncryptedPayload);
    }

    [Fact]
    public async Task RetrieveAsync_WithDesignatedAuditorGrant_ReturnsEncryptedPayload()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread);
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            "auditor-address",
            "owner-address",
            ElectionReportAccessGrantRole.DesignatedAuditor,
            now);
        var (sut, _, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
                repository
                    .Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, "auditor-address"))
                    .ReturnsAsync(auditorGrant);
            });

        var result = await sut.RetrieveAsync(new ElectionAnomalyRestrictedPayloadRetrieveRequest(
            election.ElectionId,
            "auditor-address",
            payload.PayloadReference));

        result.Success.Should().BeTrue();
        result.PayloadRecord.Should().NotBeNull();
        result.PayloadRecord!.PayloadReference.Should().Be(payload.PayloadReference);
        result.PayloadRecord.EncryptedPayload.Should().Equal(payload.EncryptedPayload);
    }

    [Fact]
    public async Task RetrieveAsync_WithUnrelatedActor_RejectsWithoutReturningPayload()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread);
        var (sut, _, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
            });

        var result = await sut.RetrieveAsync(new ElectionAnomalyRestrictedPayloadRetrieveRequest(
            election.ElectionId,
            "other-address",
            payload.PayloadReference));

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.ReadForbidden);
        result.PayloadRecord.Should().BeNull();
    }

    [Fact]
    public async Task RetrieveAsync_WithUnavailablePayload_RejectsWithoutReturningPayload()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread) with
        {
            PayloadAvailabilityStatusId = ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing,
        };
        var (sut, _, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
            });

        var result = await sut.RetrieveAsync(new ElectionAnomalyRestrictedPayloadRetrieveRequest(
            election.ElectionId,
            "owner-address",
            payload.PayloadReference));

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid);
        result.PayloadRecord.Should().BeNull();
    }

    [Fact]
    public async Task RetrieveAsync_WithStoredEncryptedHashMismatch_RejectsWithoutReturningPayload()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread) with
        {
            EncryptedPayloadHash = $"sha256:{new string('f', 64)}",
        };
        var (sut, _, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
            });

        var result = await sut.RetrieveAsync(new ElectionAnomalyRestrictedPayloadRetrieveRequest(
            election.ElectionId,
            "owner-address",
            payload.PayloadReference));

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentHashInvalid);
        result.PayloadRecord.Should().BeNull();
    }

    [Fact]
    public async Task MarkScannerStatusAsync_WithClearStatus_UpdatesPayloadAndManifestRows()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread);
        var manifest = CreateAttachmentManifest(election, thread, payload);
        ElectionAnomalyRestrictedPayloadRecord? updatedPayload = null;
        ElectionAnomalyAttachmentManifestRecord? updatedManifest = null;
        var (sut, unitOfWork, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsByPayloadReferenceAsync(payload.PayloadReference))
                    .ReturnsAsync([manifest]);
                repository
                    .Setup(x => x.UpdateAnomalyRestrictedPayloadAsync(It.IsAny<ElectionAnomalyRestrictedPayloadRecord>()))
                    .Callback<ElectionAnomalyRestrictedPayloadRecord>(record => updatedPayload = record)
                    .Returns(Task.CompletedTask);
                repository
                    .Setup(x => x.UpdateAnomalyAttachmentManifestAsync(It.IsAny<ElectionAnomalyAttachmentManifestRecord>()))
                    .Callback<ElectionAnomalyAttachmentManifestRecord>(record => updatedManifest = record)
                    .Returns(Task.CompletedTask);
            });

        var result = await sut.MarkScannerStatusAsync(new ElectionAnomalyRestrictedPayloadScannerStatusRequest(
            election.ElectionId,
            payload.PayloadReference,
            ElectionAnomalyEvidenceScannerStatusIds.Clear));

        result.Success.Should().BeTrue();
        result.UpdatedAttachmentManifestCount.Should().Be(1);
        updatedPayload.Should().NotBeNull();
        updatedPayload!.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Clear);
        updatedPayload.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Available);
        updatedPayload.LastCheckedAt.Should().NotBeNull();
        updatedManifest.Should().NotBeNull();
        updatedManifest!.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Clear);
        updatedManifest.ValidationStatusId.Should().Be(ElectionAnomalyAttachmentValidationStatusIds.Accepted);
        updatedManifest.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Available);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task MarkScannerStatusAsync_WithInvalidStatus_RejectsBeforeSaving()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread);
        var (sut, unitOfWork, repository) = CreateService(election, thread);

        var result = await sut.MarkScannerStatusAsync(new ElectionAnomalyRestrictedPayloadScannerStatusRequest(
            election.ElectionId,
            payload.PayloadReference,
            "unknown-scanner-status"));

        result.Success.Should().BeFalse();
        result.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.AttachmentScannerStatusInvalid);
        repository.Verify(
            x => x.UpdateAnomalyRestrictedPayloadAsync(It.IsAny<ElectionAnomalyRestrictedPayloadRecord>()),
            Times.Never);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Never);
    }

    [Fact]
    public async Task MarkQuarantinedAsync_UpdatesPayloadAndManifestRows()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread);
        var manifest = CreateAttachmentManifest(election, thread, payload);
        ElectionAnomalyRestrictedPayloadRecord? updatedPayload = null;
        ElectionAnomalyAttachmentManifestRecord? updatedManifest = null;
        var (sut, unitOfWork, _) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync(payload);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsByPayloadReferenceAsync(payload.PayloadReference))
                    .ReturnsAsync([manifest]);
                repository
                    .Setup(x => x.UpdateAnomalyRestrictedPayloadAsync(It.IsAny<ElectionAnomalyRestrictedPayloadRecord>()))
                    .Callback<ElectionAnomalyRestrictedPayloadRecord>(record => updatedPayload = record)
                    .Returns(Task.CompletedTask);
                repository
                    .Setup(x => x.UpdateAnomalyAttachmentManifestAsync(It.IsAny<ElectionAnomalyAttachmentManifestRecord>()))
                    .Callback<ElectionAnomalyAttachmentManifestRecord>(record => updatedManifest = record)
                    .Returns(Task.CompletedTask);
            });

        var result = await sut.MarkQuarantinedAsync(new ElectionAnomalyRestrictedPayloadQuarantineRequest(
            election.ElectionId,
            payload.PayloadReference));

        result.Success.Should().BeTrue();
        updatedPayload.Should().NotBeNull();
        updatedPayload!.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Quarantined);
        updatedPayload.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined);
        updatedManifest.Should().NotBeNull();
        updatedManifest!.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Quarantined);
        updatedManifest.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.Quarantined);
        updatedManifest.ValidationStatusId.Should().Be(ElectionAnomalyAttachmentValidationStatusIds.Rejected);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    [Fact]
    public async Task MarkPayloadMissingAsync_WithManifestButNoPayload_UpdatesManifestRows()
    {
        var now = DateTime.UtcNow;
        var election = CreateElection(now);
        var thread = CreateThread(election, "submitter-address", now);
        var payload = CreatePayload(election, thread);
        var manifest = CreateAttachmentManifest(election, thread, payload);
        ElectionAnomalyAttachmentManifestRecord? updatedManifest = null;
        var (sut, unitOfWork, repository) = CreateService(
            election,
            thread,
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyRestrictedPayloadAsync(payload.PayloadReference))
                    .ReturnsAsync((ElectionAnomalyRestrictedPayloadRecord?)null);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsByPayloadReferenceAsync(payload.PayloadReference))
                    .ReturnsAsync([manifest]);
                repository
                    .Setup(x => x.UpdateAnomalyAttachmentManifestAsync(It.IsAny<ElectionAnomalyAttachmentManifestRecord>()))
                    .Callback<ElectionAnomalyAttachmentManifestRecord>(record => updatedManifest = record)
                    .Returns(Task.CompletedTask);
            });

        var result = await sut.MarkPayloadMissingAsync(new ElectionAnomalyRestrictedPayloadMissingRequest(
            election.ElectionId,
            payload.PayloadReference));

        result.Success.Should().BeTrue();
        result.PayloadRecord.Should().BeNull();
        result.UpdatedAttachmentManifestCount.Should().Be(1);
        updatedManifest.Should().NotBeNull();
        updatedManifest!.PayloadAvailabilityStatusId.Should().Be(ElectionAnomalyPayloadAvailabilityStatusIds.PayloadMissing);
        updatedManifest.ScannerStatusId.Should().Be(ElectionAnomalyEvidenceScannerStatusIds.Pending);
        repository.Verify(
            x => x.UpdateAnomalyRestrictedPayloadAsync(It.IsAny<ElectionAnomalyRestrictedPayloadRecord>()),
            Times.Never);
        unitOfWork.Verify(x => x.CommitAsync(), Times.Once);
    }

    private static (ElectionAnomalyRestrictedPayloadStorageService Sut, Mock<IWritableUnitOfWork<ElectionsDbContext>> UnitOfWork, Mock<IElectionsRepository> Repository) CreateService(
        ElectionRecord election,
        ElectionAnomalyThreadRecord thread,
        Action<Mock<IElectionsRepository>>? setupRepository = null)
    {
        var repository = new Mock<IElectionsRepository>();
        repository
            .Setup(x => x.GetElectionAsync(election.ElectionId))
            .ReturnsAsync(election);
        repository
            .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
            .ReturnsAsync(thread);
        repository
            .Setup(x => x.GetReportAccessGrantAsync(It.IsAny<ElectionId>(), It.IsAny<string>()))
            .ReturnsAsync((ElectionReportAccessGrantRecord?)null);
        repository
            .Setup(x => x.GetAnomalyAttachmentManifestsByPayloadReferenceAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionAnomalyAttachmentManifestRecord>());
        repository
            .Setup(x => x.UpdateAnomalyRestrictedPayloadAsync(It.IsAny<ElectionAnomalyRestrictedPayloadRecord>()))
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.UpdateAnomalyAttachmentManifestAsync(It.IsAny<ElectionAnomalyAttachmentManifestRecord>()))
            .Returns(Task.CompletedTask);
        setupRepository?.Invoke(repository);

        var unitOfWork = new Mock<IWritableUnitOfWork<ElectionsDbContext>>();
        unitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);
        unitOfWork
            .Setup(x => x.CommitAsync())
            .Returns(Task.CompletedTask);
        var unitOfWorkProvider = new Mock<IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWork.Object);
        var readOnlyUnitOfWork = new Mock<IReadOnlyUnitOfWork<ElectionsDbContext>>();
        readOnlyUnitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);
        unitOfWorkProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(readOnlyUnitOfWork.Object);

        return (new ElectionAnomalyRestrictedPayloadStorageService(unitOfWorkProvider.Object), unitOfWork, repository);
    }

    private static ElectionAnomalyThreadRecord CreateThread(
        ElectionRecord election,
        string actorPublicAddress,
        DateTime now)
    {
        var clarificationRequestId = Guid.NewGuid();
        var rosterEntry = CreateLinkedRosterEntry(election.ElectionId, actorPublicAddress, now);
        var resolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            actorPublicAddress,
            now,
            linkedRosterEntry: rosterEntry);

        return new ElectionAnomalyThreadRecord(
            Guid.NewGuid(),
            election.ElectionId,
            resolution.SubmitterPersonScopeId!,
            resolution.PersonScopeDerivationVersion,
            resolution.ActorPublicAddress!,
            resolution.RoleContextId,
            resolution.RoleEvidenceTypeId!,
            resolution.RoleEvidenceReference!,
            resolution.LifecycleStateAtSubmission!.Value,
            null,
            ElectionAnomalyCategoryIds.AccessOrAuthenticationAnomaly,
            ElectionAnomalyCaseStateIds.AuthorityRequestedInformation,
            null,
            null,
            true,
            clarificationRequestId,
            now,
            now,
            SourceTransactionId: Guid.NewGuid(),
            SourceBlockHeight: null,
            SourceBlockId: null,
            CurrentThreadHash: "sha256:thread");
    }

    private static ElectionAnomalyRestrictedPayloadRecord CreatePayload(
        ElectionRecord election,
        ElectionAnomalyThreadRecord thread)
    {
        var encryptedPayload = new byte[] { 9, 8, 7, 6 };
        var contentPayload = new byte[] { 1, 3, 5 };
        var payloadId = Guid.NewGuid();

        return new ElectionAnomalyRestrictedPayloadRecord(
            payloadId,
            election.ElectionId,
            thread.Id,
            ElectionAnomalyRestrictedPayloadReferences.Create(payloadId),
            encryptedPayload,
            Sha256Ref(encryptedPayload),
            Sha256Ref(contentPayload),
            contentPayload.Length,
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            ElectionAnomalyEvidenceScannerStatusIds.Pending,
            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
            DateTime.UtcNow);
    }

    private static ElectionAnomalyAttachmentManifestRecord CreateAttachmentManifest(
        ElectionRecord election,
        ElectionAnomalyThreadRecord thread,
        ElectionAnomalyRestrictedPayloadRecord payload) =>
        new(
            Guid.NewGuid(),
            thread.Id,
            Guid.NewGuid(),
            "sha256:attachment-event",
            election.ElectionId,
            ElectionAnomalyAttachmentKindIds.AuthorityRequestedEvidence,
            payload.PayloadReference,
            payload.EncryptedPayloadHash,
            payload.ContentHash,
            payload.SizeBytes,
            payload.MimeType,
            ElectionAnomalyAttachmentValidationStatusIds.PendingScan,
            ElectionAnomalyEvidenceScannerStatusIds.Pending,
            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
            thread.OpenClarificationRequestId,
            "submitter-address",
            ElectionAnomalyRecipientRoleIds.Submitter,
            Guid.NewGuid(),
            SourceBlockHeight: null,
            SourceBlockId: null,
            RecordedAt: DateTime.UtcNow);

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

    private static string Sha256Ref(byte[] value) =>
        $"sha256:{Convert.ToHexString(SHA256.HashData(value)).ToLowerInvariant()}";
}
