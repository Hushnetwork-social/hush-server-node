using System.Text.Json;
using FluentAssertions;
using HushNode.Credentials;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushNode.MemPool;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Moq;
using Olimpo;
using Xunit;

namespace HushServerNode.Tests.Elections;

public class ElectionAnomalyEnvelopeValidationTests
{
    [Fact]
    public void ValidateAndSign_WithValidSubmitAnomalyThread_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var action = CreateSubmitAction("owner-address");
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithAnomalyActorMismatch_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction("owner-address");
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "other-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.InvalidActionSignatory);
    }

    [Fact]
    public void ValidateAndSign_WithOversizedInitialAnomalyBody_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction(
            "owner-address",
            CreateMessage(
                ElectionAnomalyMessageKindIds.InitialSubmission,
                ElectionAnomalyLimits.InitialBodyMaxCharacters + 1));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.BodyTooLong);
    }

    [Fact]
    public void ValidateAndSign_WithEmptyInitialAnomalyBody_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction(
            "owner-address",
            CreateMessage(ElectionAnomalyMessageKindIds.InitialSubmission) with
            {
                EncryptedBody = string.Empty,
            });
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.BodyRequired);
    }

    [Fact]
    public void ValidateAndSign_WithInitialAnomalyMissingSubmitterWrap_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction(
            "owner-address",
            CreateMessage(
                ElectionAnomalyMessageKindIds.InitialSubmission,
                recipientWraps:
                [
                    CreateWrap(ElectionAnomalyRecipientRoleIds.ElectionOwner, "owner-address"),
                ]));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.RecipientWrapMissing);
    }

    [Fact]
    public void ValidateAndSign_WithInitialAnomalyMissingOwnerWrap_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction(
            "owner-address",
            CreateMessage(
                ElectionAnomalyMessageKindIds.InitialSubmission,
                recipientWraps:
                [
                    CreateWrap(ElectionAnomalyRecipientRoleIds.Submitter, "owner-address"),
                ]));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.RecipientWrapMissing);
    }

    [Fact]
    public void ValidateAndSign_WithTrusteeBodyRecipientWrap_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction(
            "owner-address",
            CreateMessage(
                ElectionAnomalyMessageKindIds.InitialSubmission,
                recipientWraps:
                [
                    .. CreateVisibilityWraps(),
                    CreateWrap("trustee", "trustee-address"),
                ]));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.RecipientWrapMissing);
    }

    [Fact]
    public void ValidateAndSign_WithDesignatedAuditorRecipientWrap_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            "auditor-address",
            "owner-address",
            ElectionReportAccessGrantRole.DesignatedAuditor);
        var action = CreateSubmitAction(
            "owner-address",
            CreateMessage(
                ElectionAnomalyMessageKindIds.InitialSubmission,
                recipientWraps:
                [
                    .. CreateVisibilityWraps(),
                    CreateWrap(ElectionAnomalyRecipientRoleIds.DesignatedAuditor, "auditor-address"),
                ]));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, "auditor-address"))
                    .ReturnsAsync(auditorGrant);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithInvalidAnomalyCategory_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction("owner-address") with
        {
            CategoryId = "trustee lost key",
        };
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.CategoryInvalid);
    }

    [Fact]
    public void ValidateAndSign_WithPersistedDuplicateAnomalyThread_ReturnsStableFailure()
    {
        var election = CreateElection();
        var existingThread = CreateThread(election);
        var action = CreateSubmitAction("owner-address");
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadByPersonScopeAsync(election.ElectionId, It.IsAny<string>()))
                    .ReturnsAsync(existingThread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.DuplicateThread);
    }

    [Fact]
    public void ValidateAndSign_WithPendingDuplicateAnomalyThread_ReturnsStableFailure()
    {
        var election = CreateElection();
        var action = CreateSubmitAction("owner-address");
        var pendingAction = CreateSubmitAction("owner-address");
        var pendingSignedEnvelope = CreateSignedEnvelope(
            election.ElectionId,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            pendingAction,
            "owner-address");
        var pendingTransaction = new ValidatedTransaction<EncryptedElectionEnvelopePayload>(
            pendingSignedEnvelope,
            new SignatureInfo("validator-address", "signature"));
        var pendingEnvelope = new DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>>(
            pendingTransaction,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            JsonSerializer.Serialize(pendingAction));
        var memPoolService = new Mock<IMemPoolService>();
        memPoolService
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns([pendingTransaction]);
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyThread,
            action,
            "owner-address",
            configureCrypto: crypto =>
            {
                crypto
                    .Setup(x => x.TryDecryptValidated(
                        It.Is<AbstractTransaction>(transaction => ReferenceEquals(transaction, pendingTransaction))))
                    .Returns(pendingEnvelope);
            },
            memPoolService: memPoolService.Object);

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.DuplicateThread);
    }

    [Fact]
    public void ValidateAndSign_WithAuthorityClarificationWhenOneIsOpen_ReturnsStableFailure()
    {
        var election = CreateElection();
        var openClarificationId = Guid.NewGuid();
        var thread = CreateThread(
            election,
            hasOpenClarificationRequest: true,
            openClarificationRequestId: openClarificationId);
        var action = new RequestElectionAnomalyInformationActionPayload(
            thread.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "owner-address",
            CreateMessage(ElectionAnomalyMessageKindIds.AuthorityInformationRequest));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RequestAnomalyInformation,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.ClarificationRequestAlreadyOpen);
    }

    [Fact]
    public void ValidateAndSign_WithUnpromptedSubmitterClarification_ReturnsStableFailure()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = new SubmitElectionAnomalyInformationActionPayload(
            thread.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "owner-address",
            CreateMessage(ElectionAnomalyMessageKindIds.SubmitterInformationResponse));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyInformation,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.FollowupNotRequested);
    }

    [Fact]
    public void ValidateAndSign_WithOpenSubmitterClarification_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var openClarificationId = Guid.NewGuid();
        var thread = CreateThread(
            election,
            hasOpenClarificationRequest: true,
            openClarificationRequestId: openClarificationId);
        var action = new SubmitElectionAnomalyInformationActionPayload(
            thread.Id,
            openClarificationId,
            Guid.NewGuid(),
            "owner-address",
            CreateMessage(ElectionAnomalyMessageKindIds.SubmitterInformationResponse));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.SubmitAnomalyInformation,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithAuthorityResponseAndAllowedRecipients_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = new RecordElectionAnomalyAuthorityResponseActionPayload(
            thread.Id,
            Guid.NewGuid(),
            "owner-address",
            CreateMessage(ElectionAnomalyMessageKindIds.AuthorityResponse));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAuthorityResponse,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithAuthorityResponseMissingSubmitterWrap_ReturnsStableFailure()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = new RecordElectionAnomalyAuthorityResponseActionPayload(
            thread.Id,
            Guid.NewGuid(),
            "owner-address",
            CreateMessage(
                ElectionAnomalyMessageKindIds.AuthorityResponse,
                recipientWraps:
                [
                    CreateWrap(ElectionAnomalyRecipientRoleIds.ElectionOwner, "owner-address"),
                ]));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAuthorityResponse,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.RecipientWrapMissing);
    }

    [Fact]
    public void ValidateAndSign_WithExternalClaimantRegisteredByOwner_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var action = new RegisterExternalElectionAnomalyClaimantActionPayload(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "owner-address",
            "sha256:external-reference",
            ElectionAnomalyCategoryIds.ExternalObjectionOrComplaint,
            CreateMessage(ElectionAnomalyMessageKindIds.InitialSubmission));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RegisterExternalAnomalyClaimant,
            action,
            "owner-address");

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithSubmitterRequestedEvidenceForOpenClarification_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection(ownerPublicAddress: "authority-address");
        var openClarificationId = Guid.NewGuid();
        var thread = CreateThread(
            election,
            actorPublicAddress: "submitter-address",
            hasOpenClarificationRequest: true,
            openClarificationRequestId: openClarificationId);
        var action = CreateAttachmentManifestAction(
            thread.Id,
            "submitter-address",
            ElectionAnomalyAttachmentKindIds.AuthorityRequestedEvidence,
            clarificationRequestId: openClarificationId);
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest,
            action,
            "submitter-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsAsync(thread.Id))
                    .ReturnsAsync(Array.Empty<ElectionAnomalyAttachmentManifestRecord>());
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithSubmitterEvidenceKind_ReturnsStableFailure()
    {
        var election = CreateElection(ownerPublicAddress: "authority-address");
        var openClarificationId = Guid.NewGuid();
        var thread = CreateThread(
            election,
            actorPublicAddress: "submitter-address",
            hasOpenClarificationRequest: true,
            openClarificationRequestId: openClarificationId);
        var action = CreateAttachmentManifestAction(
            thread.Id,
            "submitter-address",
            ElectionAnomalyAttachmentKindIds.SubmitterEvidence,
            clarificationRequestId: openClarificationId);
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest,
            action,
            "submitter-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.AttachmentSubmitterNotAllowed);
    }

    [Fact]
    public void ValidateAndSign_WithOwnerAuthorityEvidence_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = CreateAttachmentManifestAction(
            thread.Id,
            "owner-address",
            ElectionAnomalyAttachmentKindIds.AuthorityEvidence);
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsAsync(thread.Id))
                    .ReturnsAsync(Array.Empty<ElectionAnomalyAttachmentManifestRecord>());
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithOwnerAuthorityEvidenceContentKeyWraps_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var auditorGrant = ElectionModelFactory.CreateReportAccessGrant(
            election.ElectionId,
            "auditor-address",
            "owner-address",
            ElectionReportAccessGrantRole.DesignatedAuditor);
        var action = CreateAttachmentManifestAction(
            thread.Id,
            "owner-address",
            ElectionAnomalyAttachmentKindIds.AuthorityEvidence) with
        {
            ContentKeyWraps =
            [
                CreateAttachmentContentKeyWrap(ElectionAnomalyRecipientRoleIds.ElectionOwner, "owner-address"),
                CreateAttachmentContentKeyWrap(ElectionAnomalyRecipientRoleIds.DesignatedAuditor, "auditor-address"),
            ],
        };
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsAsync(thread.Id))
                    .ReturnsAsync(Array.Empty<ElectionAnomalyAttachmentManifestRecord>());
                repository
                    .Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, "auditor-address"))
                    .ReturnsAsync(auditorGrant);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithOwnerAuthorityEvidenceWrongOwnerContentKeyWrap_ReturnsStableFailure()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = CreateAttachmentManifestAction(
            thread.Id,
            "owner-address",
            ElectionAnomalyAttachmentKindIds.AuthorityEvidence) with
        {
            ContentKeyWraps =
            [
                CreateAttachmentContentKeyWrap(ElectionAnomalyRecipientRoleIds.ElectionOwner, "other-address"),
            ],
        };
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestsAsync(thread.Id))
                    .ReturnsAsync(Array.Empty<ElectionAnomalyAttachmentManifestRecord>());
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.RecipientWrapMissing);
    }

    [Fact]
    public void ValidateAndSign_WithAttachmentReferenceOutsideRestrictedNamespace_ReturnsStableFailure()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = CreateAttachmentManifestAction(
            thread.Id,
            "owner-address",
            ElectionAnomalyAttachmentKindIds.AuthorityEvidence) with
        {
            EncryptedPayloadReference = "feed-attachment:123",
        };
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyAttachmentManifest,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.AttachmentPayloadReferenceInvalid);
    }

    [Fact]
    public void ValidateAndSign_WithOwnerEvidenceRedaction_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var targetManifest = CreateAttachmentManifestRecord(thread);
        var action = new RecordElectionAnomalyEvidenceRedactionActionPayload(
            thread.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "owner-address",
            ElectionAnomalyRedactionTargetKindIds.AttachmentManifest,
            targetManifest.Id.ToString(),
            ElectionAnomalyRedactionReasonIds.PersonalData,
            targetManifest.ContentHash);
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyEvidenceRedaction,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestAsync(targetManifest.Id))
                    .ReturnsAsync(targetManifest);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithRedactionOriginalHashMismatch_ReturnsStableFailure()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var targetManifest = CreateAttachmentManifestRecord(thread);
        var action = new RecordElectionAnomalyEvidenceRedactionActionPayload(
            thread.Id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            "owner-address",
            ElectionAnomalyRedactionTargetKindIds.AttachmentManifest,
            targetManifest.Id.ToString(),
            ElectionAnomalyRedactionReasonIds.PersonalData,
            Sha256Ref('d'));
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.RecordAnomalyEvidenceRedaction,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
                repository
                    .Setup(x => x.GetAnomalyAttachmentManifestAsync(targetManifest.Id))
                    .ReturnsAsync(targetManifest);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.RedactionOriginalHashInvalid);
    }

    [Fact]
    public void ValidateAndSign_WithKnownSeverityCandidate_ReturnsValidatedOuterEnvelope()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = new ClassifyElectionAnomalyThreadActionPayload(
            thread.Id,
            Guid.NewGuid(),
            "owner-address",
            CategoryId: ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
            CaseStateId: ElectionAnomalyCaseStateIds.EscalatedToGovernedDecision,
            SeverityCandidateId: ElectionAnomalySeverityCandidateIds.PotentiallyElectionBlocking,
            GovernedDecisionRef: "proposal-7");
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.ClassifyAnomalyThread,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    [Fact]
    public void ValidateAndSign_WithUnknownSeverityCandidate_ReturnsStableFailure()
    {
        var election = CreateElection();
        var thread = CreateThread(election);
        var action = new ClassifyElectionAnomalyThreadActionPayload(
            thread.Id,
            Guid.NewGuid(),
            "owner-address",
            CategoryId: ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
            CaseStateId: ElectionAnomalyCaseStateIds.UnderReview,
            SeverityCandidateId: "critical-ish");
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.ClassifyAnomalyThread,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.SeverityCandidateInvalid);
    }

    [Fact]
    public void ValidateAndSign_WithTerminalClassificationWhileClarificationOpen_ReturnsStableFailure()
    {
        var election = CreateElection();
        var thread = CreateThread(
            election,
            hasOpenClarificationRequest: true,
            openClarificationRequestId: Guid.NewGuid());
        var action = new ClassifyElectionAnomalyThreadActionPayload(
            thread.Id,
            Guid.NewGuid(),
            "owner-address",
            CaseStateId: ElectionAnomalyCaseStateIds.ResolvedNonBlocking,
            SeverityCandidateId: ElectionAnomalySeverityCandidateIds.LowOperationalImpact);
        var harness = CreateHarness(
            election,
            EncryptedElectionEnvelopeActionTypes.ClassifyAnomalyThread,
            action,
            "owner-address",
            repository =>
            {
                repository
                    .Setup(x => x.GetAnomalyThreadAsync(thread.Id))
                    .ReturnsAsync(thread);
            });

        var validatedTransaction = harness.Sut.ValidateAndSign(harness.SignedEnvelope);

        validatedTransaction.Should().BeNull();
        harness.Sut.TryTakeValidationFailure(harness.SignedEnvelope.TransactionId.Value, out var failure)
            .Should()
            .BeTrue();
        failure.Code.Should().Be(ElectionAnomalyValidationCodes.TerminalStateRequiresClosedClarification);
    }

    private static ValidationHarness CreateHarness(
        ElectionRecord election,
        string actionType,
        object actionPayload,
        string signatory,
        Action<Mock<IElectionsRepository>>? configureRepository = null,
        Action<Mock<IElectionEnvelopeCryptoService>>? configureCrypto = null,
        IMemPoolService? memPoolService = null)
    {
        var signedEnvelope = CreateSignedEnvelope(election.ElectionId, actionType, actionPayload, signatory);
        var decryptedEnvelope = new DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>(
            signedEnvelope,
            actionType,
            JsonSerializer.Serialize(actionPayload));
        var cryptoService = new Mock<IElectionEnvelopeCryptoService>();
        cryptoService
            .Setup(x => x.TryDecryptSigned(It.IsAny<AbstractTransaction>()))
            .Returns(decryptedEnvelope);
        configureCrypto?.Invoke(cryptoService);

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
        configureRepository?.Invoke(repository);

        var readOnlyUnitOfWork = new Mock<Olimpo.EntityFramework.Persistency.IReadOnlyUnitOfWork<ElectionsDbContext>>();
        readOnlyUnitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);

        var unitOfWorkProvider = new Mock<Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(readOnlyUnitOfWork.Object);

        var validatorSigningKeys = new DigitalSignature();
        var credentialsProvider = new Mock<ICredentialsProvider>();
        credentialsProvider
            .Setup(x => x.GetCredentials())
            .Returns(new CredentialsProfile
            {
                PublicSigningAddress = validatorSigningKeys.PublicAddress,
                PrivateSigningKey = validatorSigningKeys.PrivateKey,
                PublicEncryptAddress = "validator-encrypt-address",
                PrivateEncryptKey = "validator-private-encrypt-key",
            });

        var defaultMemPoolService = new Mock<IMemPoolService>();
        defaultMemPoolService
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns(Array.Empty<AbstractTransaction>());
        var sut = CreateContentHandler(
            cryptoService.Object,
            Mock.Of<ICreateElectionDraftValidationService>(),
            credentialsProvider.Object,
            unitOfWorkProvider.Object,
            Mock.Of<IElectionLifecycleService>(),
            memPoolService ?? defaultMemPoolService.Object);

        return new ValidationHarness(sut, signedEnvelope);
    }

    private static SignedTransaction<EncryptedElectionEnvelopePayload> CreateSignedEnvelope(
        ElectionId electionId,
        string actionType,
        object actionPayload,
        string signatory)
    {
        var unsignedEnvelope = EncryptedElectionEnvelopePayloadHandler.CreateNewV21(
            electionId,
            "actor-envelope",
            "election-public-key",
            "encrypted-payload",
            actionType,
            JsonSerializer.SerializeToElement(actionPayload));

        return new SignedTransaction<EncryptedElectionEnvelopePayload>(
            unsignedEnvelope,
            new SignatureInfo(signatory, "signature"));
    }

    private static SubmitElectionAnomalyThreadActionPayload CreateSubmitAction(
        string actorPublicAddress,
        ElectionAnomalyMessageEnvelopePayload? initialMessage = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            actorPublicAddress,
            ElectionAnomalyCategoryIds.TrusteeContinuityAnomaly,
            initialMessage ?? CreateMessage(ElectionAnomalyMessageKindIds.InitialSubmission));

    private static ElectionAnomalyMessageEnvelopePayload CreateMessage(
        string messageKindId,
        int characterCount = 32,
        IReadOnlyList<ElectionAnomalyRecipientWrapPayload>? recipientWraps = null) =>
        new(
            Guid.NewGuid(),
            messageKindId,
            "encrypted-body",
            "sha256:body",
            characterCount,
            recipientWraps ?? CreateVisibilityWraps());

    private static RecordElectionAnomalyAttachmentManifestActionPayload CreateAttachmentManifestAction(
        Guid anomalyThreadId,
        string actorPublicAddress,
        string attachmentKindId,
        Guid? clarificationRequestId = null) =>
        new(
            anomalyThreadId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            actorPublicAddress,
            attachmentKindId,
            ElectionAnomalyRestrictedPayloadReferences.Create(Guid.NewGuid()),
            Sha256Ref('a'),
            Sha256Ref('b'),
            1024,
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            ElectionAnomalyAttachmentValidationStatusIds.Accepted,
            clarificationRequestId);

    private static ElectionAnomalyAttachmentManifestRecord CreateAttachmentManifestRecord(
        ElectionAnomalyThreadRecord thread) =>
        new(
            Guid.NewGuid(),
            thread.Id,
            Guid.NewGuid(),
            Sha256Ref('e'),
            thread.ElectionId,
            ElectionAnomalyAttachmentKindIds.AuthorityEvidence,
            ElectionAnomalyRestrictedPayloadReferences.Create(Guid.NewGuid()),
            Sha256Ref('a'),
            Sha256Ref('b'),
            1024,
            ElectionAnomalyEvidenceMimeTypes.ApplicationPdf,
            ElectionAnomalyAttachmentValidationStatusIds.Accepted,
            ElectionAnomalyEvidenceScannerStatusIds.Clear,
            ElectionAnomalyPayloadAvailabilityStatusIds.Available,
            ClarificationRequestId: null,
            "owner-address",
            ElectionAnomalyRecipientRoleIds.ElectionOwner,
            Guid.NewGuid(),
            SourceBlockHeight: null,
            SourceBlockId: null,
            DateTime.UtcNow);

    private static string Sha256Ref(char fill) =>
        $"sha256:{new string(fill, 64)}";

    private static IReadOnlyList<ElectionAnomalyRecipientWrapPayload> CreateVisibilityWraps(
        string submitterPublicAddress = "owner-address",
        string ownerPublicAddress = "owner-address") =>
    [
        CreateWrap(ElectionAnomalyRecipientRoleIds.Submitter, submitterPublicAddress),
        CreateWrap(ElectionAnomalyRecipientRoleIds.ElectionOwner, ownerPublicAddress),
    ];

    private static ElectionAnomalyRecipientWrapPayload CreateWrap(
        string recipientRoleId,
        string recipientPublicAddress) =>
        new(
            recipientRoleId,
            recipientPublicAddress,
            $"{recipientRoleId}-key-fingerprint",
            $"{recipientRoleId}-encrypted-content-key",
            "x25519-aes-gcm");

    private static ElectionAnomalyAttachmentContentKeyWrapPayload CreateAttachmentContentKeyWrap(
        string recipientRoleId,
        string recipientPublicAddress) =>
        new(
            recipientRoleId,
            recipientPublicAddress,
            $"{recipientRoleId}-key-fingerprint",
            $"{recipientRoleId}-encrypted-content-key",
            "x25519-aes-gcm");

    private static ElectionAnomalyThreadRecord CreateThread(
        ElectionRecord election,
        string actorPublicAddress = "owner-address",
        bool hasOpenClarificationRequest = false,
        Guid? openClarificationRequestId = null)
    {
        var reportAccessGrant = string.Equals(election.OwnerPublicAddress, actorPublicAddress, StringComparison.Ordinal)
            ? null
            : ElectionModelFactory.CreateReportAccessGrant(
                election.ElectionId,
                actorPublicAddress,
                election.OwnerPublicAddress,
                ElectionReportAccessGrantRole.DesignatedAuditor);
        var roleResolution = ElectionAnomalyAuthorization.ResolveActorSubmitter(
            election,
            actorPublicAddress,
            DateTime.UtcNow,
            reportAccessGrant: reportAccessGrant);
        roleResolution.IsResolved.Should().BeTrue();

        return new ElectionAnomalyThreadRecord(
            Guid.NewGuid(),
            election.ElectionId,
            roleResolution.SubmitterPersonScopeId!,
            roleResolution.PersonScopeDerivationVersion,
            actorPublicAddress,
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

    private static ElectionRecord CreateElection(string ownerPublicAddress = "owner-address") =>
        ElectionModelFactory.CreateDraftRecord(
            electionId: ElectionId.NewElectionId,
            title: "Board Election",
            shortDescription: "Annual board vote",
            ownerPublicAddress: ownerPublicAddress,
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

    private static EncryptedElectionEnvelopeContentHandler CreateContentHandler(
        IElectionEnvelopeCryptoService cryptoService,
        ICreateElectionDraftValidationService validationService,
        ICredentialsProvider credentialsProvider,
        Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        IElectionLifecycleService lifecycleService,
        IMemPoolService memPoolService) =>
        new(
            cryptoService,
            validationService,
            new UpdateElectionDraftContentHandler(credentialsProvider, unitOfWorkProvider),
            new InviteElectionTrusteeContentHandler(credentialsProvider, unitOfWorkProvider),
            new RevokeElectionTrusteeInvitationContentHandler(credentialsProvider, unitOfWorkProvider),
            new StartElectionGovernedProposalContentHandler(credentialsProvider, unitOfWorkProvider, lifecycleService),
            new ApproveElectionGovernedProposalContentHandler(credentialsProvider, unitOfWorkProvider),
            new RetryElectionGovernedProposalExecutionContentHandler(credentialsProvider, unitOfWorkProvider),
            new OpenElectionContentHandler(credentialsProvider, unitOfWorkProvider, lifecycleService),
            new CloseElectionContentHandler(credentialsProvider, unitOfWorkProvider),
            new FinalizeElectionContentHandler(credentialsProvider, unitOfWorkProvider),
            credentialsProvider,
            unitOfWorkProvider,
            memPoolService,
            new ElectionCeremonyOptions(
                EnableDevCeremonyProfiles: true,
                ApprovedRegistryRelativePath: "ignored",
                RequiredRolloutVersion: "test"),
            ElectionEnvelopeOptions.Default);

    private sealed record ValidationHarness(
        EncryptedElectionEnvelopeContentHandler Sut,
        SignedTransaction<EncryptedElectionEnvelopePayload> SignedEnvelope);
}
