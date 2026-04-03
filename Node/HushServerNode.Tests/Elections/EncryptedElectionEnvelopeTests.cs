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
using SharedTrusteeReference = HushShared.Elections.Model.ElectionTrusteeReference;

namespace HushServerNode.Tests.Elections;

public class EncryptedElectionEnvelopeTests
{
    [Fact]
    public void TryDecryptSigned_WithValidCreateDraftEnvelope_ReturnsTypedAction()
    {
        var nodeEncryptKeys = new EncryptKeys();
        var actorEncryptKeys = new EncryptKeys();
        var electionEncryptKeys = new EncryptKeys();
        var draft = CreateDraftSpecification();
        var actionEnvelope = new EncryptedElectionActionEnvelope(
            EncryptedElectionEnvelopeActionTypes.CreateDraft,
            JsonSerializer.SerializeToElement(new CreateElectionDraftActionPayload(
                "owner-address",
                "initial draft",
                draft)));
        var unsignedTransaction = EncryptedElectionEnvelopePayloadHandler.CreateNew(
            ElectionId.NewElectionId,
            EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
            EncryptKeys.Encrypt(electionEncryptKeys.PrivateKey, nodeEncryptKeys.PublicKey),
            EncryptKeys.Encrypt(electionEncryptKeys.PrivateKey, actorEncryptKeys.PublicKey),
            EncryptKeys.Encrypt(JsonSerializer.Serialize(actionEnvelope), electionEncryptKeys.PublicKey));
        var signedTransaction = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            unsignedTransaction,
            new SignatureInfo("owner-address", "signature"));

        var credentialsProvider = new Mock<ICredentialsProvider>();
        credentialsProvider
            .Setup(x => x.GetCredentials())
            .Returns(new CredentialsProfile
            {
                PublicSigningAddress = "validator-address",
                PrivateSigningKey = new DigitalSignature().PrivateKey,
                PublicEncryptAddress = nodeEncryptKeys.PublicKey,
                PrivateEncryptKey = nodeEncryptKeys.PrivateKey,
            });

        var sut = new ElectionEnvelopeCryptoService(credentialsProvider.Object);

        var decryptedEnvelope = sut.TryDecryptSigned(signedTransaction);

        decryptedEnvelope.Should().NotBeNull();
        decryptedEnvelope!.ActionType.Should().Be(EncryptedElectionEnvelopeActionTypes.CreateDraft);
        var actionPayload = decryptedEnvelope.DeserializeAction<CreateElectionDraftActionPayload>();
        actionPayload.Should().NotBeNull();
        actionPayload!.OwnerPublicAddress.Should().Be("owner-address");
        actionPayload.SnapshotReason.Should().Be("initial draft");
        actionPayload.Draft.Title.Should().Be(draft.Title);
    }

    [Fact]
    public void ValidateAndSign_WithValidCreateDraftEnvelope_ReturnsValidatedOuterEnvelope()
    {
        var validatorSigningKeys = new DigitalSignature();
        var validatorEncryptKeys = new EncryptKeys();
        var unsignedEnvelope = EncryptedElectionEnvelopePayloadHandler.CreateNew(
            ElectionId.NewElectionId,
            EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
            "node-envelope",
            "actor-envelope",
            "encrypted-payload");
        var signedEnvelope = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            unsignedEnvelope,
            new SignatureInfo("owner-address", "signature"));

        var cryptoService = new Mock<IElectionEnvelopeCryptoService>();
        cryptoService
            .Setup(x => x.TryDecryptSigned(It.IsAny<AbstractTransaction>()))
            .Returns(new DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>(
                signedEnvelope,
                EncryptedElectionEnvelopeActionTypes.CreateDraft,
                JsonSerializer.Serialize(new CreateElectionDraftActionPayload(
                    "owner-address",
                    "initial draft",
                    CreateDraftSpecification()))));

        var validationService = new Mock<ICreateElectionDraftValidationService>();
        validationService
            .Setup(x => x.IsValid(
                It.Is<CreateElectionDraftPayload>(payload =>
                    payload.ElectionId == signedEnvelope.Payload.ElectionId &&
                    payload.OwnerPublicAddress == "owner-address" &&
                    payload.SnapshotReason == "initial draft"),
                "owner-address"))
            .Returns((CreateElectionDraftPayload _, string _) => true);

        var credentialsProvider = new Mock<ICredentialsProvider>();
        credentialsProvider
            .Setup(x => x.GetCredentials())
            .Returns(new CredentialsProfile
            {
                PublicSigningAddress = validatorSigningKeys.PublicAddress,
                PrivateSigningKey = validatorSigningKeys.PrivateKey,
                PublicEncryptAddress = validatorEncryptKeys.PublicKey,
                PrivateEncryptKey = validatorEncryptKeys.PrivateKey,
            });

        var lifecycleService = new Mock<IElectionLifecycleService>();
        var unitOfWorkProvider = new Mock<Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext>>();
        var sut = CreateContentHandler(
            cryptoService.Object,
            validationService.Object,
            credentialsProvider.Object,
            unitOfWorkProvider.Object,
            lifecycleService.Object);

        var validatedTransaction = sut.ValidateAndSign(signedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
        ((ValidatedTransaction<EncryptedElectionEnvelopePayload>)validatedTransaction!)
            .ValidatorSignature
            .Signatory
            .Should()
            .Be(validatorSigningKeys.PublicAddress);
    }

    [Fact]
    public void ValidateAndSign_WithValidCreateReportAccessGrantEnvelope_ReturnsValidatedOuterEnvelope()
    {
        var validatorSigningKeys = new DigitalSignature();
        var validatorEncryptKeys = new EncryptKeys();
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
            acknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
            ]);
        var unsignedEnvelope = EncryptedElectionEnvelopePayloadHandler.CreateNew(
            election.ElectionId,
            EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
            "node-envelope",
            "actor-envelope",
            "encrypted-payload");
        var signedEnvelope = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            unsignedEnvelope,
            new SignatureInfo("owner-address", "signature"));

        var cryptoService = new Mock<IElectionEnvelopeCryptoService>();
        cryptoService
            .Setup(x => x.TryDecryptSigned(It.IsAny<AbstractTransaction>()))
            .Returns(new DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>(
                signedEnvelope,
                EncryptedElectionEnvelopeActionTypes.CreateReportAccessGrant,
                JsonSerializer.Serialize(new CreateElectionReportAccessGrantActionPayload(
                    "owner-address",
                    "auditor-address"))));

        var validationService = new Mock<ICreateElectionDraftValidationService>();
        var credentialsProvider = new Mock<ICredentialsProvider>();
        credentialsProvider
            .Setup(x => x.GetCredentials())
            .Returns(new CredentialsProfile
            {
                PublicSigningAddress = validatorSigningKeys.PublicAddress,
                PrivateSigningKey = validatorSigningKeys.PrivateKey,
                PublicEncryptAddress = validatorEncryptKeys.PublicKey,
                PrivateEncryptKey = validatorEncryptKeys.PrivateKey,
            });

        var repository = new Mock<IElectionsRepository>();
        repository.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        repository.Setup(x => x.GetTrusteeInvitationsAsync(election.ElectionId))
            .ReturnsAsync(Array.Empty<ElectionTrusteeInvitationRecord>());
        repository.Setup(x => x.GetReportAccessGrantAsync(election.ElectionId, "auditor-address"))
            .ReturnsAsync((ElectionReportAccessGrantRecord?)null);

        var readOnlyUnitOfWork = new Mock<Olimpo.EntityFramework.Persistency.IReadOnlyUnitOfWork<ElectionsDbContext>>();
        readOnlyUnitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);

        var unitOfWorkProvider = new Mock<Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(readOnlyUnitOfWork.Object);

        var lifecycleService = new Mock<IElectionLifecycleService>();
        var sut = CreateContentHandler(
            cryptoService.Object,
            validationService.Object,
            credentialsProvider.Object,
            unitOfWorkProvider.Object,
            lifecycleService.Object);

        var validatedTransaction = sut.ValidateAndSign(signedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
        ((ValidatedTransaction<EncryptedElectionEnvelopePayload>)validatedTransaction!)
            .ValidatorSignature
            .Signatory
            .Should()
            .Be(validatorSigningKeys.PublicAddress);
    }

    [Fact]
    public void ValidateAndSign_WithFinalizedClaimRosterEntryEnvelope_ReturnsValidatedOuterEnvelope()
    {
        var validatorSigningKeys = new DigitalSignature();
        var validatorEncryptKeys = new EncryptKeys();
        var finalizedAt = DateTime.UtcNow.AddMinutes(-1);
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
            acknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
            ]) with
        {
            LifecycleState = ElectionLifecycleState.Finalized,
            ClosedAt = finalizedAt.AddMinutes(-2),
            FinalizedAt = finalizedAt,
            LastUpdatedAt = finalizedAt,
        };
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
            election.ElectionId,
            "1001",
            ElectionRosterContactType.Email,
            "voter-1001@example.org");
        var unsignedEnvelope = EncryptedElectionEnvelopePayloadHandler.CreateNew(
            election.ElectionId,
            EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
            "node-envelope",
            "actor-envelope",
            "encrypted-payload");
        var signedEnvelope = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            unsignedEnvelope,
            new SignatureInfo("voter-address", "signature"));

        var cryptoService = new Mock<IElectionEnvelopeCryptoService>();
        cryptoService
            .Setup(x => x.TryDecryptSigned(It.IsAny<AbstractTransaction>()))
            .Returns(new DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>(
                signedEnvelope,
                EncryptedElectionEnvelopeActionTypes.ClaimRosterEntry,
                JsonSerializer.Serialize(new ClaimElectionRosterEntryActionPayload(
                    "voter-address",
                    "1001",
                    "1111"))));

        var validationService = new Mock<ICreateElectionDraftValidationService>();
        var credentialsProvider = new Mock<ICredentialsProvider>();
        credentialsProvider
            .Setup(x => x.GetCredentials())
            .Returns(new CredentialsProfile
            {
                PublicSigningAddress = validatorSigningKeys.PublicAddress,
                PrivateSigningKey = validatorSigningKeys.PrivateKey,
                PublicEncryptAddress = validatorEncryptKeys.PublicKey,
                PrivateEncryptKey = validatorEncryptKeys.PrivateKey,
            });

        var repository = new Mock<IElectionsRepository>();
        repository.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        repository.Setup(x => x.GetRosterEntryAsync(election.ElectionId, "1001")).ReturnsAsync(rosterEntry);
        repository.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
            .ReturnsAsync((ElectionRosterEntryRecord?)null);

        var readOnlyUnitOfWork = new Mock<Olimpo.EntityFramework.Persistency.IReadOnlyUnitOfWork<ElectionsDbContext>>();
        readOnlyUnitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);

        var unitOfWorkProvider = new Mock<Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(readOnlyUnitOfWork.Object);

        var lifecycleService = new Mock<IElectionLifecycleService>();
        var sut = CreateContentHandler(
            cryptoService.Object,
            validationService.Object,
            credentialsProvider.Object,
            unitOfWorkProvider.Object,
            lifecycleService.Object);

        var validatedTransaction = sut.ValidateAndSign(signedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
        ((ValidatedTransaction<EncryptedElectionEnvelopePayload>)validatedTransaction!)
            .ValidatorSignature
            .Signatory
            .Should()
            .Be(validatorSigningKeys.PublicAddress);
    }

    [Fact]
    public void ValidateAndSign_WithPendingAcceptBallotCastSubmissionInMemPool_ReturnsNull()
    {
        var openedAt = DateTime.UtcNow.AddMinutes(-10);
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
        var openArtifactId = Guid.NewGuid();
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
            acknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
            ]) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
            OpenArtifactId = openArtifactId,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            recordedByPublicAddress: "owner-address",
            recordedAt: openedAt,
            frozenEligibleVoterSetHash: [1, 2, 3, 4],
            ceremonySnapshot: ceremonySnapshot) with
        {
            Id = openArtifactId,
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
            rosterEntry.OrganizationVoterId,
            "voter-address",
            "commitment-hash-1",
            openedAt.AddMinutes(2));

        var signedEnvelope = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            EncryptedElectionEnvelopePayloadHandler.CreateNew(
                election.ElectionId,
                EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
                "node-envelope",
                "actor-envelope",
                "encrypted-payload"),
            new SignatureInfo("voter-address", "signature"));
        var signedActionEnvelope = new DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>(
            signedEnvelope,
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast,
            JsonSerializer.Serialize(new AcceptElectionBallotCastActionPayload(
                "voter-address",
                "cast-key-1",
                "ciphertext",
                "proof-bundle",
                "nullifier-1",
                openArtifact.Id,
                [1, 2, 3, 4],
                ceremonySnapshot.CeremonyVersionId,
                ceremonySnapshot.ProfileId,
                ceremonySnapshot.TallyPublicKeyFingerprint)));

        var signedPendingEnvelope = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            EncryptedElectionEnvelopePayloadHandler.CreateNew(
                election.ElectionId,
                EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
                "node-envelope",
                "actor-envelope",
                "encrypted-payload"),
            new SignatureInfo("voter-address", "signature"));
        var pendingTransaction = new ValidatedTransaction<EncryptedElectionEnvelopePayload>(
            signedPendingEnvelope,
            new SignatureInfo("validator-address", "signature"));
        var pendingActionEnvelope = new DecryptedElectionEnvelope<ValidatedTransaction<EncryptedElectionEnvelopePayload>>(
            pendingTransaction,
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast,
            JsonSerializer.Serialize(new AcceptElectionBallotCastActionPayload(
                "voter-address",
                "cast-key-1",
                "ciphertext",
                "proof-bundle",
                "nullifier-1",
                openArtifact.Id,
                [1, 2, 3, 4],
                ceremonySnapshot.CeremonyVersionId,
                ceremonySnapshot.ProfileId,
                ceremonySnapshot.TallyPublicKeyFingerprint)));

        var cryptoService = new Mock<IElectionEnvelopeCryptoService>();
        cryptoService
            .Setup(x => x.TryDecryptSigned(It.IsAny<AbstractTransaction>()))
            .Returns(signedActionEnvelope);
        cryptoService
            .Setup(x => x.TryDecryptValidated(It.Is<AbstractTransaction>(transaction => ReferenceEquals(transaction, pendingTransaction))))
            .Returns(pendingActionEnvelope);

        var validationService = new Mock<ICreateElectionDraftValidationService>();
        var credentialsProvider = new Mock<ICredentialsProvider>();
        credentialsProvider
            .Setup(x => x.GetCredentials())
            .Returns(new CredentialsProfile
            {
                PublicSigningAddress = "validator-address",
                PrivateSigningKey = new DigitalSignature().PrivateKey,
                PublicEncryptAddress = "validator-encrypt-address",
                PrivateEncryptKey = "validator-private-encrypt-key",
            });

        var repository = new Mock<IElectionsRepository>();
        repository.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        repository.Setup(x => x.GetCastIdempotencyRecordAsync(election.ElectionId, It.IsAny<string>()))
            .ReturnsAsync((ElectionCastIdempotencyRecord?)null);
        repository.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
            .ReturnsAsync(rosterEntry);
        repository.Setup(x => x.GetCommitmentRegistrationAsync(election.ElectionId, rosterEntry.OrganizationVoterId))
            .ReturnsAsync(commitmentRegistration);
        repository.Setup(x => x.GetCheckoffConsumptionAsync(election.ElectionId, rosterEntry.OrganizationVoterId))
            .ReturnsAsync((ElectionCheckoffConsumptionRecord?)null);
        repository.Setup(x => x.GetParticipationRecordAsync(election.ElectionId, rosterEntry.OrganizationVoterId))
            .ReturnsAsync((ElectionParticipationRecord?)null);
        repository.Setup(x => x.GetAcceptedBallotByNullifierAsync(election.ElectionId, "nullifier-1"))
            .ReturnsAsync((ElectionAcceptedBallotRecord?)null);
        repository.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId))
            .ReturnsAsync([openArtifact]);

        var readOnlyUnitOfWork = new Mock<Olimpo.EntityFramework.Persistency.IReadOnlyUnitOfWork<ElectionsDbContext>>();
        readOnlyUnitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);

        var unitOfWorkProvider = new Mock<Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(readOnlyUnitOfWork.Object);

        var lifecycleService = new Mock<IElectionLifecycleService>();
        var memPoolService = new Mock<IMemPoolService>();
        memPoolService
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns([pendingTransaction]);

        var sut = CreateContentHandler(
            cryptoService.Object,
            validationService.Object,
            credentialsProvider.Object,
            unitOfWorkProvider.Object,
            lifecycleService.Object,
            memPoolService.Object);

        var validatedTransaction = sut.ValidateAndSign(signedEnvelope);

        validatedTransaction.Should().BeNull();
    }

    [Fact]
    public void ValidateAndSign_WithLegacyAdminOnlyOpenBoundaryWithoutStoredSnapshot_UsesSyntheticProtectedTallyBinding()
    {
        var openedAt = DateTime.UtcNow.AddMinutes(-10);
        var openArtifactId = Guid.NewGuid();
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
            acknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
            ]) with
        {
            LifecycleState = ElectionLifecycleState.Open,
            OpenedAt = openedAt,
            LastUpdatedAt = openedAt,
            OpenArtifactId = openArtifactId,
        };
        var openArtifact = ElectionModelFactory.CreateBoundaryArtifact(
            ElectionBoundaryArtifactType.Open,
            election,
            recordedByPublicAddress: "owner-address",
            recordedAt: openedAt,
            frozenEligibleVoterSetHash: [1, 2, 3, 4]) with
        {
            Id = openArtifactId,
        };
        var syntheticBinding = ElectionProtectedTallyBinding.BuildAdminOnlyProtectedTallyBindingSnapshot(election);
        var rosterEntry = ElectionModelFactory.CreateRosterEntry(
                election.ElectionId,
                "1001",
                ElectionRosterContactType.Email,
                "voter-1001@example.org")
            .FreezeAtOpen(openedAt)
            .LinkToActor("voter-address", openedAt.AddMinutes(1));
        var commitmentRegistration = ElectionModelFactory.CreateCommitmentRegistrationRecord(
            election.ElectionId,
            rosterEntry.OrganizationVoterId,
            "voter-address",
            "commitment-hash-1",
            openedAt.AddMinutes(2));

        var signedEnvelope = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            EncryptedElectionEnvelopePayloadHandler.CreateNew(
                election.ElectionId,
                EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
                "node-envelope",
                "actor-envelope",
                "encrypted-payload"),
            new SignatureInfo("voter-address", "signature"));
        var signedActionEnvelope = new DecryptedElectionEnvelope<SignedTransaction<EncryptedElectionEnvelopePayload>>(
            signedEnvelope,
            EncryptedElectionEnvelopeActionTypes.AcceptBallotCast,
            JsonSerializer.Serialize(new AcceptElectionBallotCastActionPayload(
                "voter-address",
                "cast-key-1",
                "ciphertext",
                "proof-bundle",
                "nullifier-1",
                openArtifact.Id,
                [1, 2, 3, 4],
                syntheticBinding.CeremonyVersionId,
                syntheticBinding.ProfileId,
                syntheticBinding.TallyPublicKeyFingerprint)));

        var cryptoService = new Mock<IElectionEnvelopeCryptoService>();
        cryptoService
            .Setup(x => x.TryDecryptSigned(It.IsAny<AbstractTransaction>()))
            .Returns(signedActionEnvelope);

        var validationService = new Mock<ICreateElectionDraftValidationService>();
        var credentialsProvider = new Mock<ICredentialsProvider>();
        credentialsProvider
            .Setup(x => x.GetCredentials())
            .Returns(new CredentialsProfile
            {
                PublicSigningAddress = "validator-address",
                PrivateSigningKey = new DigitalSignature().PrivateKey,
                PublicEncryptAddress = "validator-encrypt-address",
                PrivateEncryptKey = "validator-private-encrypt-key",
            });

        var repository = new Mock<IElectionsRepository>();
        repository.Setup(x => x.GetElectionAsync(election.ElectionId)).ReturnsAsync(election);
        repository.Setup(x => x.GetCastIdempotencyRecordAsync(election.ElectionId, It.IsAny<string>()))
            .ReturnsAsync((ElectionCastIdempotencyRecord?)null);
        repository.Setup(x => x.GetRosterEntryByLinkedActorAsync(election.ElectionId, "voter-address"))
            .ReturnsAsync(rosterEntry);
        repository.Setup(x => x.GetCommitmentRegistrationAsync(election.ElectionId, rosterEntry.OrganizationVoterId))
            .ReturnsAsync(commitmentRegistration);
        repository.Setup(x => x.GetCheckoffConsumptionAsync(election.ElectionId, rosterEntry.OrganizationVoterId))
            .ReturnsAsync((ElectionCheckoffConsumptionRecord?)null);
        repository.Setup(x => x.GetParticipationRecordAsync(election.ElectionId, rosterEntry.OrganizationVoterId))
            .ReturnsAsync((ElectionParticipationRecord?)null);
        repository.Setup(x => x.GetAcceptedBallotByNullifierAsync(election.ElectionId, "nullifier-1"))
            .ReturnsAsync((ElectionAcceptedBallotRecord?)null);
        repository.Setup(x => x.GetBoundaryArtifactsAsync(election.ElectionId))
            .ReturnsAsync([openArtifact]);

        var readOnlyUnitOfWork = new Mock<Olimpo.EntityFramework.Persistency.IReadOnlyUnitOfWork<ElectionsDbContext>>();
        readOnlyUnitOfWork
            .Setup(x => x.GetRepository<IElectionsRepository>())
            .Returns(repository.Object);

        var unitOfWorkProvider = new Mock<Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext>>();
        unitOfWorkProvider
            .Setup(x => x.CreateReadOnly())
            .Returns(readOnlyUnitOfWork.Object);

        var lifecycleService = new Mock<IElectionLifecycleService>();
        var sut = CreateContentHandler(
            cryptoService.Object,
            validationService.Object,
            credentialsProvider.Object,
            unitOfWorkProvider.Object,
            lifecycleService.Object);

        var validatedTransaction = sut.ValidateAndSign(signedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
    }

    private static ElectionDraftSpecification CreateDraftSpecification() =>
        new(
            Title: "Board Election",
            ShortDescription: "Annual board vote",
            ExternalReferenceCode: "ORG-2026-01",
            ElectionClass: ElectionClass.OrganizationalRemoteVoting,
            BindingStatus: ElectionBindingStatus.Binding,
            GovernanceMode: ElectionGovernanceMode.AdminOnly,
            DisclosureMode: ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
            OutcomeRule: new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            ApprovedClientApplications:
            [
                new ApprovedClientApplicationRecord("hushsocial", "1.0.0"),
            ],
            ProtocolOmegaVersion: "omega-v1.0.0",
            ReportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
            OwnerOptions:
            [
                new ElectionOptionDefinition("option-a", "Alice", "First option", 1, false),
                new ElectionOptionDefinition("option-b", "Bob", "Second option", 2, false),
            ],
            AcknowledgedWarningCodes:
            [
                ElectionWarningCode.LowAnonymitySet,
            ]);

    private static EncryptedElectionEnvelopeContentHandler CreateContentHandler(
        IElectionEnvelopeCryptoService cryptoService,
        ICreateElectionDraftValidationService validationService,
        ICredentialsProvider credentialsProvider,
        Olimpo.EntityFramework.Persistency.IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
        IElectionLifecycleService lifecycleService,
        IMemPoolService? memPoolService = null) =>
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
            memPoolService ?? Mock.Of<IMemPoolService>(),
            new ElectionCeremonyOptions(
                EnableDevCeremonyProfiles: true,
                ApprovedRegistryRelativePath: "ignored",
                RequiredRolloutVersion: "test"));
}
