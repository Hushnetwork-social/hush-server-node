using System.Text.Json;
using FluentAssertions;
using HushNode.Caching;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyIndexingTests
{
    private static readonly BlockId TestBlockId = new(Guid.Parse("c258ec0b-7816-4533-8b8d-1a91b61e1f04"));

    [Fact]
    public async Task HandleAsync_WithSubmitAnomalyThreadEnvelope_PersistsInitialThreadEventMessageWrapsAndAction()
    {
        var election = CreateElection();
        var action = CreateSubmitAction("owner-address");
        var transaction = CreateValidatedTransaction(election.ElectionId, "owner-address");
        ElectionAnomalyThreadRecord? savedThread = null;
        ElectionAnomalyThreadEventRecord? savedEvent = null;
        ElectionAnomalyMessageEnvelopeRecord? savedMessage = null;
        IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>? savedWraps = null;
        ElectionAnomalyActionRecord? savedAction = null;
        var repository = CreateRepository(election);
        repository
            .Setup(x => x.SaveAnomalyThreadWithInitialEventAsync(
                It.IsAny<ElectionAnomalyThreadRecord>(),
                It.IsAny<ElectionAnomalyThreadEventRecord>(),
                It.IsAny<ElectionAnomalyMessageEnvelopeRecord?>(),
                It.IsAny<IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>>()))
            .Callback<ElectionAnomalyThreadRecord, ElectionAnomalyThreadEventRecord, ElectionAnomalyMessageEnvelopeRecord?, IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>>(
                (thread, threadEvent, message, wraps) =>
                {
                    savedThread = thread;
                    savedEvent = threadEvent;
                    savedMessage = message;
                    savedWraps = wraps;
                })
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.SaveAnomalyActionRecordAsync(It.IsAny<ElectionAnomalyActionRecord>()))
            .Callback<ElectionAnomalyActionRecord>(record => savedAction = record)
            .Returns(Task.CompletedTask);
        var sut = CreateIndexStrategy(transaction, EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread, action, repository.Object);

        await sut.HandleAsync(transaction);

        savedThread.Should().NotBeNull();
        savedThread!.Id.Should().Be(action.AnomalyThreadId);
        savedThread.CurrentCategoryId.Should().Be(action.CategoryId);
        savedThread.CurrentCaseStateId.Should().Be(ElectionAnomalyCaseStateIds.Submitted);
        savedThread.SourceTransactionId.Should().Be(transaction.TransactionId.Value);
        savedEvent.Should().NotBeNull();
        savedEvent!.Sequence.Should().Be(1);
        savedEvent.EventTypeId.Should().Be(ElectionAnomalyEventTypeIds.ThreadSubmitted);
        savedEvent.SourceBlockHeight.Should().Be(42);
        savedEvent.SourceBlockId.Should().Be(TestBlockId.Value);
        savedMessage.Should().NotBeNull();
        savedMessage!.Id.Should().Be(action.InitialMessage.MessageId);
        savedWraps.Should().HaveCount(2);
        savedWraps!.Select(x => x.RecipientRoleId)
            .Should()
            .Contain([ElectionAnomalyRecipientRoleIds.Submitter, ElectionAnomalyRecipientRoleIds.ElectionOwner]);
        savedAction.Should().NotBeNull();
        savedAction!.ActionOutcomeId.Should().Be(ElectionAnomalyActionOutcomeIds.Accepted);
    }

    [Fact]
    public async Task HandleAsync_WithDuplicateSubmitAnomalyThreadEnvelope_RecordsIgnoredDuplicateWithoutCreatingThread()
    {
        var election = CreateElection();
        var existingThread = CreateThread(election);
        var action = CreateSubmitAction("owner-address");
        var transaction = CreateValidatedTransaction(election.ElectionId, "owner-address");
        ElectionAnomalyActionRecord? savedAction = null;
        var repository = CreateRepository(election);
        repository
            .Setup(x => x.GetAnomalyThreadByPersonScopeAsync(election.ElectionId, It.IsAny<string>()))
            .ReturnsAsync(existingThread);
        repository
            .Setup(x => x.SaveAnomalyActionRecordAsync(It.IsAny<ElectionAnomalyActionRecord>()))
            .Callback<ElectionAnomalyActionRecord>(record => savedAction = record)
            .Returns(Task.CompletedTask);
        var sut = CreateIndexStrategy(transaction, EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread, action, repository.Object);

        await sut.HandleAsync(transaction);

        repository.Verify(
            x => x.SaveAnomalyThreadWithInitialEventAsync(
                It.IsAny<ElectionAnomalyThreadRecord>(),
                It.IsAny<ElectionAnomalyThreadEventRecord>(),
                It.IsAny<ElectionAnomalyMessageEnvelopeRecord?>(),
                It.IsAny<IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>>()),
            Times.Never);
        savedAction.Should().NotBeNull();
        savedAction!.ActionOutcomeId.Should().Be(ElectionAnomalyActionOutcomeIds.IgnoredDuplicate);
        savedAction.ValidationCode.Should().Be(ElectionAnomalyValidationCodes.DuplicateThread);
    }

    [Fact]
    public async Task HandleAsync_WithAuthorityClarificationRequest_AppendsEventAndOpensThreadRequest()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var initialEvent = CreateExistingEvent(thread, sequence: 1);
        var clarificationRequestId = Guid.NewGuid();
        var action = new RequestElectionAnomalyInformationActionPayload(
            thread.Id,
            clarificationRequestId,
            Guid.NewGuid(),
            "owner-address",
            CreateMessage(ElectionAnomalyMessageKindIds.AuthorityInformationRequest));
        var transaction = CreateValidatedTransaction(election.ElectionId, "owner-address");
        ElectionAnomalyThreadEventRecord? savedEvent = null;
        ElectionAnomalyThreadRecord? updatedThread = null;
        ElectionAnomalyMessageEnvelopeRecord? savedMessage = null;
        var repository = CreateRepository(election);
        repository.Setup(x => x.GetAnomalyThreadAsync(thread.Id)).ReturnsAsync(thread);
        repository.Setup(x => x.GetLatestAnomalyThreadEventAsync(thread.Id)).ReturnsAsync(initialEvent);
        repository.Setup(x => x.GetAnomalyThreadEventsAsync(thread.Id)).ReturnsAsync([initialEvent]);
        repository
            .Setup(x => x.SaveAnomalyThreadEventAsync(It.IsAny<ElectionAnomalyThreadEventRecord>()))
            .Callback<ElectionAnomalyThreadEventRecord>(record => savedEvent = record)
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.SaveAnomalyMessageEnvelopeAsync(It.IsAny<ElectionAnomalyMessageEnvelopeRecord>()))
            .Callback<ElectionAnomalyMessageEnvelopeRecord>(record => savedMessage = record)
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.SaveAnomalyRecipientWrapsAsync(It.IsAny<IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>>()))
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.UpdateAnomalyThreadAsync(It.IsAny<ElectionAnomalyThreadRecord>()))
            .Callback<ElectionAnomalyThreadRecord>(record => updatedThread = record)
            .Returns(Task.CompletedTask);
        repository
            .Setup(x => x.SaveAnomalyActionRecordAsync(It.IsAny<ElectionAnomalyActionRecord>()))
            .Returns(Task.CompletedTask);
        var sut = CreateIndexStrategy(transaction, EncryptedElectionEnvelopeActionTypes.RequestAnomalyInformation, action, repository.Object);

        await sut.HandleAsync(transaction);

        savedEvent.Should().NotBeNull();
        savedEvent!.Sequence.Should().Be(2);
        savedEvent.PreviousEventHash.Should().Be(initialEvent.EventHash);
        savedEvent.EventTypeId.Should().Be(ElectionAnomalyEventTypeIds.AuthorityInformationRequested);
        savedMessage.Should().NotBeNull();
        savedMessage!.MessageKindId.Should().Be(ElectionAnomalyMessageKindIds.AuthorityInformationRequest);
        updatedThread.Should().NotBeNull();
        updatedThread!.HasOpenClarificationRequest.Should().BeTrue();
        updatedThread.OpenClarificationRequestId.Should().Be(clarificationRequestId);
        updatedThread.CurrentCaseStateId.Should().Be(ElectionAnomalyCaseStateIds.AuthorityRequestedInformation);
    }

    [Fact]
    public async Task HandleAsync_WithSubmitterClarificationResponse_AppendsEventAndClosesOpenRequest()
    {
        var election = CreateElection();
        var clarificationRequestId = Guid.NewGuid();
        var thread = CreateThread(
            election,
            hasOpenClarificationRequest: true,
            openClarificationRequestId: clarificationRequestId);
        var initialEvent = CreateExistingEvent(thread, sequence: 1);
        var action = new SubmitElectionAnomalyInformationActionPayload(
            thread.Id,
            clarificationRequestId,
            Guid.NewGuid(),
            "owner-address",
            CreateMessage(ElectionAnomalyMessageKindIds.SubmitterInformationResponse));
        var transaction = CreateValidatedTransaction(election.ElectionId, "owner-address");
        ElectionAnomalyThreadRecord? updatedThread = null;
        var repository = CreateRepository(election);
        repository.Setup(x => x.GetAnomalyThreadAsync(thread.Id)).ReturnsAsync(thread);
        repository.Setup(x => x.GetLatestAnomalyThreadEventAsync(thread.Id)).ReturnsAsync(initialEvent);
        repository.Setup(x => x.GetAnomalyThreadEventsAsync(thread.Id)).ReturnsAsync([initialEvent]);
        repository.Setup(x => x.SaveAnomalyThreadEventAsync(It.IsAny<ElectionAnomalyThreadEventRecord>())).Returns(Task.CompletedTask);
        repository.Setup(x => x.SaveAnomalyMessageEnvelopeAsync(It.IsAny<ElectionAnomalyMessageEnvelopeRecord>())).Returns(Task.CompletedTask);
        repository.Setup(x => x.SaveAnomalyRecipientWrapsAsync(It.IsAny<IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>>())).Returns(Task.CompletedTask);
        repository
            .Setup(x => x.UpdateAnomalyThreadAsync(It.IsAny<ElectionAnomalyThreadRecord>()))
            .Callback<ElectionAnomalyThreadRecord>(record => updatedThread = record)
            .Returns(Task.CompletedTask);
        repository.Setup(x => x.SaveAnomalyActionRecordAsync(It.IsAny<ElectionAnomalyActionRecord>())).Returns(Task.CompletedTask);
        var sut = CreateIndexStrategy(transaction, EncryptedElectionEnvelopeActionTypes.SubmitAnomalyInformation, action, repository.Object);

        await sut.HandleAsync(transaction);

        updatedThread.Should().NotBeNull();
        updatedThread!.HasOpenClarificationRequest.Should().BeFalse();
        updatedThread.OpenClarificationRequestId.Should().BeNull();
        updatedThread.CurrentCaseStateId.Should().Be(ElectionAnomalyCaseStateIds.SubmitterInformationProvided);
    }

    [Fact]
    public async Task HandleAsync_WithAuditorGrantAfterAnomalyMessages_AddsPendingBackfillRecipientWrap()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var message = CreateMessageRecord(thread);
        var action = new CreateElectionReportAccessGrantActionPayload("owner-address", "auditor-address");
        var transaction = CreateValidatedTransaction(election.ElectionId, "owner-address");
        IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>? savedWraps = null;
        var repository = CreateRepository(election);
        repository.Setup(x => x.GetAnomalyThreadsAsync(election.ElectionId)).ReturnsAsync([thread]);
        repository.Setup(x => x.GetAnomalyMessageEnvelopesAsync(thread.Id)).ReturnsAsync([message]);
        repository
            .Setup(x => x.GetAnomalyRecipientWrapsAsync(thread.Id))
            .ReturnsAsync(
            [
                new ElectionAnomalyRecipientWrapRecord(
                    Guid.NewGuid(),
                    message.Id,
                    thread.Id,
                    thread.ElectionId,
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    "owner-address",
                    "owner-key-fingerprint",
                    "encrypted-content-key",
                    "x25519-aes-gcm",
                    ElectionAnomalyRecipientWrapStatusIds.Available,
                    DateTime.UtcNow),
            ]);
        repository
            .Setup(x => x.SaveAnomalyRecipientWrapsAsync(It.IsAny<IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>>()))
            .Callback<IReadOnlyCollection<ElectionAnomalyRecipientWrapRecord>>(wraps => savedWraps = wraps)
            .Returns(Task.CompletedTask);
        var lifecycleService = new Mock<IElectionLifecycleService>();
        lifecycleService
            .Setup(x => x.CreateReportAccessGrantAsync(It.IsAny<CreateElectionReportAccessGrantRequest>()))
            .ReturnsAsync(ElectionCommandResult.Success(
                election,
                reportAccessGrant: ElectionModelFactory.CreateReportAccessGrant(
                    election.ElectionId,
                    "auditor-address",
                    "owner-address")));
        var sut = CreateIndexStrategy(
            transaction,
            EncryptedElectionEnvelopeActionTypes.CreateReportAccessGrant,
            action,
            repository.Object,
            lifecycleService.Object);

        await sut.HandleAsync(transaction);

        savedWraps.Should().NotBeNull();
        savedWraps.Should().ContainSingle();
        var wrap = savedWraps!.Single();
        wrap.MessageEnvelopeId.Should().Be(message.Id);
        wrap.RecipientRoleId.Should().Be(ElectionAnomalyRecipientRoleIds.DesignatedAuditor);
        wrap.RecipientPublicAddress.Should().Be("auditor-address");
        wrap.WrapStatusId.Should().Be(ElectionAnomalyRecipientWrapStatusIds.PendingBackfill);
    }

    private static Mock<IElectionsRepository> CreateRepository(ElectionRecord election)
    {
        var repository = new Mock<IElectionsRepository>();
        repository.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        repository
            .Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, It.IsAny<string>()))
            .ReturnsAsync((ElectionRosterEntryRecord?)null);
        repository
            .Setup(x => x.GetAcceptedTrusteeInvitationsByActorAsync(It.IsAny<string>()))
            .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        repository
            .Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, It.IsAny<string>()))
            .ReturnsAsync((ElectionReportAccessGrantRecord?)null);
        repository
            .Setup(x => x.GetAnomalyThreadByPersonScopeAsync(election.ElectionId, It.IsAny<string>()))
            .ReturnsAsync((ElectionAnomalyThreadRecord?)null);
        return repository;
    }

    private static EncryptedElectionEnvelopeIndexStrategy CreateIndexStrategy<TAction>(
        ValidatedTransaction<EncryptedElectionEnvelopePayload> transaction,
        string actionType,
        TAction action,
        IElectionsRepository repository,
        IElectionLifecycleService? lifecycleService = null)
    {
        var decryptedEnvelope = new DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>>(
            transaction,
            actionType,
            JsonSerializer.Serialize(action));
        var cryptoService = new Mock<IElectionEnvelopeCryptoService>();
        cryptoService
            .Setup(x => x.TryDecryptValidated(It.Is<AbstractTransaction>(candidate => ReferenceEquals(candidate, transaction))))
            .Returns(decryptedEnvelope);

        var blockchainCache = new Mock<IBlockchainCache>();
        blockchainCache.SetupGet(x => x.LastBlockIndex).Returns(new BlockIndex(42));
        blockchainCache.SetupGet(x => x.CurrentBlockId).Returns(TestBlockId);

        var unitOfWork = new Mock<IWritableUnitOfWork<ElectionsDbContext>>();
        unitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository);
        unitOfWork.Setup(x => x.CommitAsync()).Returns(Task.CompletedTask);

        var unitOfWorkProvider = new Mock<IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateWritable())
            .Returns(unitOfWork.Object);

        return new EncryptedElectionEnvelopeIndexStrategy(
            cryptoService.Object,
            lifecycleService ?? Mock.Of<IElectionLifecycleService>(),
            blockchainCache.Object,
            unitOfWorkProvider.Object,
            Mock.Of<ILogger<EncryptedElectionEnvelopeIndexStrategy>>());
    }

    private static ValidatedTransaction<EncryptedElectionEnvelopePayload> CreateValidatedTransaction(
        ElectionId electionId,
        string signatory)
    {
        var signedTransaction = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            EncryptedElectionEnvelopePayloadHandler.CreateNewV21(
                electionId,
                "actor-envelope",
                "election-public-key",
                "encrypted-payload",
                EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
                JsonSerializer.SerializeToElement(new { marker = "test" })),
            new SignatureInfo(signatory, "signature"));

        return new ValidatedTransaction<EncryptedElectionEnvelopePayload>(
            signedTransaction,
            new SignatureInfo("validator-address", "validator-signature"));
    }

    private static SubmitElectionAnomalyThreadActionPayload CreateSubmitAction(string actorPublicAddress) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            actorPublicAddress,
            ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
            CreateMessage(ElectionAnomalyMessageKindIds.InitialSubmission));

    private static ElectionAnomalyMessageEnvelopePayload CreateMessage(string messageKindId) =>
        new(
            Guid.NewGuid(),
            messageKindId,
            "encrypted-body",
            "sha256:body",
            24,
            [
                new ElectionAnomalyRecipientWrapPayload(
                    ElectionAnomalyRecipientRoleIds.Submitter,
                    "owner-address",
                    "submitter-key-fingerprint",
                    "submitter-encrypted-content-key",
                    "x25519-aes-gcm"),
                new ElectionAnomalyRecipientWrapPayload(
                    ElectionAnomalyRecipientRoleIds.ElectionOwner,
                    "owner-address",
                    "owner-key-fingerprint",
                    "encrypted-content-key",
                    "x25519-aes-gcm"),
            ]);

    private static ElectionAnomalyMessageEnvelopeRecord CreateMessageRecord(ElectionAnomalyThreadRecord thread) =>
        new(
            Guid.NewGuid(),
            thread.Id,
            Guid.NewGuid(),
            thread.ElectionId,
            ElectionAnomalyMessageKindIds.InitialSubmission,
            "encrypted-body",
            "sha256:body",
            PlaintextBodyHash: null,
            PlaintextCharacterCount: 24,
            EncryptionAlgorithm: "x25519-aes-gcm",
            DateTime.UtcNow);

    private static ElectionAnomalyThreadRecord CreateThread(
        ElectionRecord election,
        bool hasOpenClarificationRequest = false,
        Guid? openClarificationRequestId = null)
    {
        var roleResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            "owner-address",
            DateTime.UtcNow);
        roleResolution.IsResolved.Should().BeTrue();

        return new ElectionAnomalyThreadRecord(
            Guid.NewGuid(),
            election.ElectionId,
            roleResolution.SubmitterPersonScopeId!,
            roleResolution.PersonScopeDerivationVersion,
            "owner-address",
            roleResolution.RoleContextId,
            roleResolution.RoleEvidenceTypeId!,
            roleResolution.RoleEvidenceReference!,
            election.LifecycleState,
            election.AnomalySubmissionWindowClosesAt,
            ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
            ElectionAnomalyCaseStateIds.Submitted,
            SeverityCandidateId: null,
            GovernedDecisionRef: null,
            hasOpenClarificationRequest,
            openClarificationRequestId,
            DateTime.UtcNow.AddMinutes(-5),
            DateTime.UtcNow.AddMinutes(-5),
            Guid.NewGuid(),
            SourceBlockHeight: null,
            SourceBlockId: null,
            "sha256:thread");
    }

    private static ElectionAnomalyThreadEventRecord CreateExistingEvent(
        ElectionAnomalyThreadRecord thread,
        int sequence)
    {
        var eventRecord = new ElectionAnomalyThreadEventRecord(
            Guid.NewGuid(),
            thread.Id,
            thread.ElectionId,
            sequence,
            ElectionAnomalyEventTypeIds.ThreadSubmitted,
            "{}",
            EventHash: "pending",
            PreviousEventHash: null,
            Guid.NewGuid(),
            thread.SourceTransactionId,
            SourceBlockHeight: null,
            SourceBlockId: null,
            thread.SubmitterActorPublicAddress,
            thread.CreatedAt);

        return eventRecord with
        {
            EventHash = ElectionAnomalyEventHasher.ComputeEventHash(eventRecord),
        };
    }

    private static ElectionRecord CreateElection() =>
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
                new ElectionOptionDefinition("option-a", "Alice", "First option", 1, false),
                new ElectionOptionDefinition("option-b", "Bob", "Second option", 2, false),
            ],
            acknowledgedWarningCodes: []) with
        {
            AnomalySubmissionWindowClosesAt = DateTime.UtcNow.AddHours(1),
        };
}
