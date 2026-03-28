using System.Text.Json;
using FluentAssertions;
using HushNode.Credentials;
using HushNode.Elections;
using HushNode.Elections.Storage;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Moq;
using Olimpo;
using Xunit;

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
        var sut = new EncryptedElectionEnvelopeContentHandler(
            cryptoService.Object,
            validationService.Object,
            new UpdateElectionDraftContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object),
            new InviteElectionTrusteeContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object),
            new RevokeElectionTrusteeInvitationContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object),
            new StartElectionGovernedProposalContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object, lifecycleService.Object),
            new ApproveElectionGovernedProposalContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object),
            new RetryElectionGovernedProposalExecutionContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object),
            new OpenElectionContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object, lifecycleService.Object),
            new CloseElectionContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object),
            new FinalizeElectionContentHandler(credentialsProvider.Object, unitOfWorkProvider.Object),
            credentialsProvider.Object,
            unitOfWorkProvider.Object,
            new ElectionCeremonyOptions(
                EnableDevCeremonyProfiles: true,
                ApprovedRegistryRelativePath: "ignored",
                RequiredRolloutVersion: "test"));

        var validatedTransaction = sut.ValidateAndSign(signedEnvelope);

        validatedTransaction.Should().BeOfType<ValidatedTransaction<EncryptedElectionEnvelopePayload>>();
        ((ValidatedTransaction<EncryptedElectionEnvelopePayload>)validatedTransaction!)
            .ValidatorSignature
            .Signatory
            .Should()
            .Be(validatorSigningKeys.PublicAddress);
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
}
