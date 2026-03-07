using FluentAssertions;
using HushNode.Credentials;
using HushNode.MemPool;
using HushNode.Reactions.Crypto;
using HushNode.Reactions.Storage;
using HushNode.Reactions.Tests.Fixtures;
using HushNode.Reactions.ZK;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

public class NewReactionContentHandlerTests
{
    [Fact]
    public void ValidateAndSign_WithDevModeCircuitVersion_DoesNotBypassConfiguredVerifier()
    {
        var (sut, zkVerifierMock, _, _, _) = CreateHandler();
        var transaction = CreateTransaction("dev-mode-v1");

        zkVerifierMock
            .Setup(x => x.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<PublicInputs>(),
                "dev-mode-v1"))
            .ReturnsAsync(VerifyResult.Failure("INVALID_PROOF", "Rejected"));

        var result = sut.ValidateAndSign(transaction);

        result.Should().BeNull();
        zkVerifierMock.Verify(x => x.VerifyAsync(
            It.IsAny<byte[]>(),
            It.IsAny<PublicInputs>(),
            "dev-mode-v1"), Times.Once);
    }

    [Fact]
    public void ValidateAndSign_WithSuccessfulVerification_ReturnsValidatedTransaction()
    {
        var (sut, zkVerifierMock, _, _, credentials) = CreateHandler();
        var transaction = CreateTransaction("omega-v1.0.0");

        zkVerifierMock
            .Setup(x => x.VerifyAsync(
                It.IsAny<byte[]>(),
                It.IsAny<PublicInputs>(),
                "omega-v1.0.0"))
            .ReturnsAsync(VerifyResult.Success());

        var result = sut.ValidateAndSign(transaction);

        result.Should().NotBeNull();
        result.Should().BeOfType<ValidatedTransaction<NewReactionPayload>>();

        var validated = (ValidatedTransaction<NewReactionPayload>)result!;
        validated.ValidatorSignature.Signatory.Should().Be(credentials.PublicSigningAddress);
    }

    [Fact]
    public void ValidateAndSign_WhenSameUserAlreadyHasPendingReactionForTarget_ShouldReturnNullWithoutVerifying()
    {
        var pendingTransaction = CreateValidatedTransaction("omega-v1.0.0");
        var (sut, zkVerifierMock, _, memPoolMock, _) = CreateHandler();
        memPoolMock
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns(new AbstractTransaction[] { pendingTransaction });

        var transaction = CreateTransaction(
            "omega-v1.0.0",
            pendingTransaction.UserSignature!.Signatory,
            pendingTransaction.Payload.FeedId,
            pendingTransaction.Payload.MessageId);

        var result = sut.ValidateAndSign(transaction);

        result.Should().BeNull();
        zkVerifierMock.Verify(x => x.VerifyAsync(
            It.IsAny<byte[]>(),
            It.IsAny<PublicInputs>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndSign_WithMalformedCircuitVersionFormat_ShouldReturnNullWithoutVerifying()
    {
        var (sut, zkVerifierMock, _, _, _) = CreateHandler();
        var transaction = CreateTransaction("omega-v1");

        var result = sut.ValidateAndSign(transaction);

        result.Should().BeNull();
        zkVerifierMock.Verify(x => x.VerifyAsync(
            It.IsAny<byte[]>(),
            It.IsAny<PublicInputs>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndSign_WithUnexpectedBackupLength_ShouldReturnNullWithoutVerifying()
    {
        var (sut, zkVerifierMock, _, _, _) = CreateHandler();
        var transaction = CreateTransaction("omega-v1.0.0", encryptedEmojiBackup: new byte[2]);

        var result = sut.ValidateAndSign(transaction);

        result.Should().BeNull();
        zkVerifierMock.Verify(x => x.VerifyAsync(
            It.IsAny<byte[]>(),
            It.IsAny<PublicInputs>(),
            It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void ValidateAndSign_WithUnexpectedCiphertextCoordinateLength_ShouldReturnNullWithoutVerifying()
    {
        var (sut, zkVerifierMock, _, _, _) = CreateHandler();
        var transaction = CreateTransaction(
            "omega-v1.0.0",
            ciphertextC1X: Enumerable.Range(0, 6)
                .Select(i => i == 0 ? new byte[31] : CreateValidXCoordinateBytes())
                .ToArray());

        var result = sut.ValidateAndSign(transaction);

        result.Should().BeNull();
        zkVerifierMock.Verify(x => x.VerifyAsync(
            It.IsAny<byte[]>(),
            It.IsAny<PublicInputs>(),
            It.IsAny<string>()), Times.Never);
    }

    private static (
        NewReactionContentHandler sut,
        Mock<IZkVerifier> zkVerifierMock,
        Mock<IFeedInfoProvider> feedInfoProviderMock,
        Mock<IMemPoolService> memPoolServiceMock,
        CredentialsProfile credentials)
        CreateHandler()
    {
        var credentials = new CredentialsProfile
        {
            PublicSigningAddress = "validator-address",
            PrivateSigningKey = "1111111111111111111111111111111111111111111111111111111111111111"
        };

        var credentialProviderMock = new Mock<ICredentialsProvider>();
        credentialProviderMock.Setup(x => x.GetCredentials()).Returns(credentials);

        var zkVerifierMock = new Mock<IZkVerifier>();
        var membershipServiceMock = new Mock<IMembershipService>();
        var feedInfoProviderMock = new Mock<IFeedInfoProvider>();
        var memPoolServiceMock = new Mock<IMemPoolService>();

        var curve = new BabyJubJubCurve();
        var point = curve.Generator;
        var feedId = TestDataFactory.CreateFeedId();

        feedInfoProviderMock
            .Setup(x => x.GetFeedPublicKeyAsync(It.IsAny<FeedId>()))
            .ReturnsAsync(point);

        feedInfoProviderMock
            .Setup(x => x.GetAuthorCommitmentAsync(It.IsAny<FeedMessageId>()))
            .ReturnsAsync(TestDataFactory.CreateCommitment());

        membershipServiceMock
            .Setup(x => x.GetRecentRootsAsync(It.IsAny<FeedId>(), It.IsAny<int>()))
            .ReturnsAsync(new[]
            {
                new MerkleRootHistory(
                    1,
                    feedId,
                    TestDataFactory.CreateCommitment(),
                    100,
                    DateTime.UtcNow)
            });

        memPoolServiceMock
            .Setup(x => x.PeekPendingValidatedTransactions())
            .Returns([]);

        var sut = new NewReactionContentHandler(
            credentialProviderMock.Object,
            zkVerifierMock.Object,
            membershipServiceMock.Object,
            feedInfoProviderMock.Object,
            memPoolServiceMock.Object,
            Mock.Of<ILogger<NewReactionContentHandler>>());

        return (sut, zkVerifierMock, feedInfoProviderMock, memPoolServiceMock, credentials);
    }

    private static SignedTransaction<NewReactionPayload> CreateTransaction(
        string circuitVersion,
        string signatory = "user-address",
        FeedId? feedId = null,
        FeedMessageId? messageId = null,
        byte[]? encryptedEmojiBackup = null,
        byte[][]? ciphertextC1X = null,
        byte[][]? ciphertextC1Y = null,
        byte[][]? ciphertextC2X = null,
        byte[][]? ciphertextC2Y = null)
    {
        var curve = new BabyJubJubCurve();
        var pointX = PadTo32Bytes(curve.Generator.X.ToByteArray(isUnsigned: true, isBigEndian: true));
        var pointY = PadTo32Bytes(curve.Generator.Y.ToByteArray(isUnsigned: true, isBigEndian: true));

        var payload = new NewReactionPayload(
            feedId ?? TestDataFactory.CreateFeedId(),
            messageId ?? TestDataFactory.CreateMessageId(),
            TestDataFactory.CreateNullifier(),
            ciphertextC1X ?? Enumerable.Range(0, 6).Select(_ => pointX).ToArray(),
            ciphertextC1Y ?? Enumerable.Range(0, 6).Select(_ => pointY).ToArray(),
            ciphertextC2X ?? Enumerable.Range(0, 6).Select(_ => pointX).ToArray(),
            ciphertextC2Y ?? Enumerable.Range(0, 6).Select(_ => pointY).ToArray(),
            new byte[256],
            circuitVersion,
            encryptedEmojiBackup ?? new byte[32]);

        var unsigned = new UnsignedTransaction<NewReactionPayload>(
            new TransactionId(Guid.NewGuid()),
            NewReactionPayloadHandler.NewReactionPayloadKind,
            Timestamp.Current,
            payload,
            1000);

        return new SignedTransaction<NewReactionPayload>(
            unsigned,
            new SignatureInfo(signatory, "user-signature"));
    }

    private static ValidatedTransaction<NewReactionPayload> CreateValidatedTransaction(string circuitVersion)
    {
        var signed = CreateTransaction(circuitVersion);
        return new ValidatedTransaction<NewReactionPayload>(
            signed,
            new SignatureInfo("validator-address", "validator-signature"));
    }

    private static byte[] CreateValidXCoordinateBytes()
    {
        var curve = new BabyJubJubCurve();
        return PadTo32Bytes(curve.Generator.X.ToByteArray(isUnsigned: true, isBigEndian: true));
    }

    private static byte[] PadTo32Bytes(byte[] input)
    {
        if (input.Length >= 32)
        {
            return input[..32];
        }

        var result = new byte[32];
        Array.Copy(input, 0, result, 32 - input.Length, input.Length);
        return result;
    }
}
