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

public class EncryptedElectionEnvelopeIndexStrategyTests
{
    [Fact]
    [Trait("Category", "FEAT-114")]
    public async Task HandleAsync_WithRegisterPreparedBallotCommitmentEnvelope_ForwardsSp04Request()
    {
        var electionId = ElectionId.NewElectionId;
        var transaction = CreateValidatedTransaction(electionId);
        var ballotDefinitionHash = new byte[] { 1, 2, 3, 4 };
        var preparedBallotId = Guid.NewGuid();
        var precommittedAt = DateTime.UtcNow.AddMinutes(-2);
        var action = new RegisterPreparedBallotCommitmentActionPayload(
            "voter-address",
            preparedBallotId,
            "prepared-hash-1",
            1,
            ballotDefinitionHash,
            ElectionSp04ProfileIds.ChallengeSpoilV1,
            "proof-statement-1",
            precommittedAt);
        var election = CreateElection(electionId);
        RegisterPreparedBallotCommitmentRequest? capturedRequest = null;

        var lifecycleService = new Mock<IElectionLifecycleService>();
        lifecycleService
            .Setup(x => x.RegisterPreparedBallotCommitmentAsync(It.IsAny<RegisterPreparedBallotCommitmentRequest>()))
            .Callback<RegisterPreparedBallotCommitmentRequest>(request => capturedRequest = request)
            .ReturnsAsync(new ElectionPreparedBallotCommitmentResult { IsSuccess = true, Election = election });

        var sut = CreateIndexStrategy(transaction, EncryptedElectionEnvelopeActionTypes.RegisterPreparedBallotCommitment, action, lifecycleService.Object);

        await sut.HandleAsync(transaction);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ElectionId.Should().Be(electionId);
        capturedRequest.ActorPublicAddress.Should().Be("voter-address");
        capturedRequest.PreparedBallotId.Should().Be(preparedBallotId);
        capturedRequest.PreparedBallotHash.Should().Be("prepared-hash-1");
        capturedRequest.BallotDefinitionVersion.Should().Be(1);
        capturedRequest.BallotDefinitionHash.Should().Equal(ballotDefinitionHash);
        capturedRequest.CeremonyProfileId.Should().Be(ElectionSp04ProfileIds.ChallengeSpoilV1);
        capturedRequest.ProofStatementId.Should().Be("proof-statement-1");
        capturedRequest.PrecommittedAt.Should().Be(precommittedAt);
        capturedRequest.SourceTransactionId.Should().Be(transaction.TransactionId.Value);
        capturedRequest.SourceBlockHeight.Should().Be(42);
        capturedRequest.SourceBlockId.Should().Be(TestBlockId.Value);
    }

    [Fact]
    [Trait("Category", "FEAT-114")]
    public async Task HandleAsync_WithSpoilPreparedBallotEnvelope_ForwardsSp04Request()
    {
        var electionId = ElectionId.NewElectionId;
        var transaction = CreateValidatedTransaction(electionId);
        var preparedBallotId = Guid.NewGuid();
        var spoiledAt = DateTime.UtcNow.AddMinutes(-1);
        var action = new SpoilPreparedBallotActionPayload(
            "voter-address",
            preparedBallotId,
            "prepared-hash-1",
            "spoiled-transcript-hash-1",
            "spoil-record-hash-1",
            "local-verifier-v1",
            spoiledAt);
        var election = CreateElection(electionId);
        SpoilPreparedBallotRequest? capturedRequest = null;

        var lifecycleService = new Mock<IElectionLifecycleService>();
        lifecycleService
            .Setup(x => x.SpoilPreparedBallotAsync(It.IsAny<SpoilPreparedBallotRequest>()))
            .Callback<SpoilPreparedBallotRequest>(request => capturedRequest = request)
            .ReturnsAsync(new ElectionSpoilPreparedBallotResult { IsSuccess = true, Election = election });

        var sut = CreateIndexStrategy(transaction, EncryptedElectionEnvelopeActionTypes.SpoilPreparedBallot, action, lifecycleService.Object);

        await sut.HandleAsync(transaction);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.ElectionId.Should().Be(electionId);
        capturedRequest.ActorPublicAddress.Should().Be("voter-address");
        capturedRequest.PreparedBallotId.Should().Be(preparedBallotId);
        capturedRequest.PreparedBallotHash.Should().Be("prepared-hash-1");
        capturedRequest.SpoiledTranscriptHash.Should().Be("spoiled-transcript-hash-1");
        capturedRequest.SpoilRecordHash.Should().Be("spoil-record-hash-1");
        capturedRequest.LocalVerifierVersion.Should().Be("local-verifier-v1");
        capturedRequest.SpoiledAt.Should().Be(spoiledAt);
        capturedRequest.SourceTransactionId.Should().Be(transaction.TransactionId.Value);
        capturedRequest.SourceBlockHeight.Should().Be(42);
        capturedRequest.SourceBlockId.Should().Be(TestBlockId.Value);
    }

    [Fact]
    [Trait("Category", "FEAT-114")]
    public async Task HandleAsync_WithAcceptBallotCastEnvelope_ForwardsSp04BindingFields()
    {
        var electionId = ElectionId.NewElectionId;
        var transaction = CreateValidatedTransaction(electionId);
        var openArtifactId = Guid.NewGuid();
        var ceremonyVersionId = Guid.NewGuid();
        var preparedBallotId = Guid.NewGuid();
        var eligibleSetHash = new byte[] { 5, 6, 7, 8 };
        var ballotDefinitionHash = new byte[] { 9, 10, 11, 12 };
        var action = new AcceptElectionBallotCastActionPayload(
            "voter-address",
            "cast-idempotency-key",
            "encrypted-ballot-package",
            "proof-bundle",
            "nullifier-1",
            openArtifactId,
            eligibleSetHash,
            ceremonyVersionId,
            "dkg-profile-1",
            "tally-fingerprint-1",
            preparedBallotId,
            "prepared-hash-1",
            "receipt-commitment-1",
            "sha256:receipt-secret:v1",
            1,
            ballotDefinitionHash);
        var election = CreateElection(electionId);
        AcceptElectionBallotCastRequest? capturedRequest = null;

        var lifecycleService = new Mock<IElectionLifecycleService>();
        lifecycleService
            .Setup(x => x.AcceptBallotCastAsync(It.IsAny<AcceptElectionBallotCastRequest>()))
            .Callback<AcceptElectionBallotCastRequest>(request => capturedRequest = request)
            .ReturnsAsync(new ElectionCastAcceptanceResult { IsSuccess = true, Election = election });

        var sut = CreateIndexStrategy(transaction, EncryptedElectionEnvelopeActionTypes.AcceptBallotCast, action, lifecycleService.Object);

        await sut.HandleAsync(transaction);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.PreparedBallotId.Should().Be(preparedBallotId);
        capturedRequest.PreparedBallotHash.Should().Be("prepared-hash-1");
        capturedRequest.ReceiptCommitment.Should().Be("receipt-commitment-1");
        capturedRequest.ReceiptCommitmentScheme.Should().Be("sha256:receipt-secret:v1");
        capturedRequest.BallotDefinitionVersion.Should().Be(1);
        capturedRequest.BallotDefinitionHash.Should().Equal(ballotDefinitionHash);
    }

    private static readonly BlockId TestBlockId = new(Guid.Parse("ab9f92a3-5e14-4d73-8755-99708f48e03c"));

    private static EncryptedElectionEnvelopeIndexStrategy CreateIndexStrategy<TAction>(
        ValidatedTransaction<EncryptedElectionEnvelopePayload> transaction,
        string actionType,
        TAction action,
        IElectionLifecycleService lifecycleService)
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

        return new EncryptedElectionEnvelopeIndexStrategy(
            cryptoService.Object,
            lifecycleService,
            blockchainCache.Object,
            Mock.Of<IUnitOfWorkProvider<ElectionsDbContext>>(),
            Mock.Of<ILogger<EncryptedElectionEnvelopeIndexStrategy>>());
    }

    private static ValidatedTransaction<EncryptedElectionEnvelopePayload> CreateValidatedTransaction(ElectionId electionId)
    {
        var signedTransaction = new SignedTransaction<EncryptedElectionEnvelopePayload>(
            EncryptedElectionEnvelopePayloadHandler.CreateNew(
                electionId,
                EncryptedElectionEnvelopePayloadHandler.CurrentEnvelopeVersion,
                "node-envelope",
                "actor-envelope",
                "encrypted-payload"),
            new SignatureInfo("voter-address", "signature"));

        return new ValidatedTransaction<EncryptedElectionEnvelopePayload>(
            signedTransaction,
            new SignatureInfo("validator-address", "validator-signature"));
    }

    private static ElectionRecord CreateElection(ElectionId electionId) =>
        ElectionModelFactory.CreateDraftRecord(
            electionId,
            "Board Election",
            "Annual board vote",
            "owner-address",
            "ORG-2026-01",
            ElectionClass.OrganizationalRemoteVoting,
            ElectionBindingStatus.Binding,
            ElectionGovernanceMode.AdminOnly,
            ElectionDisclosureMode.FinalResultsOnly,
            ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
            VoteUpdatePolicy.SingleSubmissionOnly,
            EligibilitySourceType.OrganizationImportedRoster,
            EligibilityMutationPolicy.FrozenAtOpen,
            new OutcomeRuleDefinition(
                OutcomeRuleKind.SingleWinner,
                "single_winner",
                SeatCount: 1,
                BlankVoteCountsForTurnout: true,
                BlankVoteExcludedFromWinnerSelection: true,
                BlankVoteExcludedFromThresholdDenominator: false,
                TieResolutionRule: "tie_unresolved",
                CalculationBasis: "highest_non_blank_votes"),
            [new ApprovedClientApplicationRecord("hushsocial", "1.0.0")],
            "omega-v1.0.0",
            ReportingPolicy.DefaultPhaseOnePackage,
            ReviewWindowPolicy.NoReviewWindow,
            ownerOptions:
            [
                new ElectionOptionDefinition("option-a", "Alice", "First option", 1, false),
                new ElectionOptionDefinition("option-b", "Bob", "Second option", 2, false),
            ],
            acknowledgedWarningCodes: [ElectionWarningCode.LowAnonymitySet]);
}
