using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.Tests.Fixtures;

/// <summary>
/// Factory for creating test data used across NewGroupFeed tests.
/// </summary>
public static class TestDataFactory
{
    private static readonly Random Random = new(42); // Deterministic for reproducibility

    public static FeedId CreateFeedId() => new(Guid.NewGuid());

    public static string CreateAddress()
    {
        var bytes = new byte[20];
        Random.NextBytes(bytes);
        return "0x" + Convert.ToHexString(bytes).ToLower();
    }

    public static string CreateEncryptedKey()
    {
        var bytes = new byte[256];
        Random.NextBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    public static NewGroupFeedPayload CreateValidPayload(
        string creatorAddress,
        int participantCount = 1,
        string? title = null,
        string? description = null,
        bool isPublic = false)
    {
        var feedId = CreateFeedId();
        var participants = new List<GroupFeedParticipant>
        {
            new(feedId, creatorAddress, ParticipantType.Member, CreateEncryptedKey(), 0)
        };

        // Add additional participants
        for (int i = 1; i < participantCount; i++)
        {
            participants.Add(new GroupFeedParticipant(
                feedId,
                CreateAddress(),
                ParticipantType.Member,
                CreateEncryptedKey(),
                0));
        }

        return new NewGroupFeedPayload(
            feedId,
            title ?? "Test Group",
            description ?? "Test Description",
            isPublic,
            participants.ToArray());
    }

    public static SignedTransaction<NewGroupFeedPayload> CreateSignedTransaction(
        NewGroupFeedPayload payload,
        string creatorAddress)
    {
        var signature = new SignatureInfo(
            creatorAddress,
            CreateSignatureString());

        var unsignedTx = new UnsignedTransaction<NewGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedPayloadHandler.NewGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);

        return new SignedTransaction<NewGroupFeedPayload>(unsignedTx, signature);
    }

    public static ValidatedTransaction<NewGroupFeedPayload> CreateValidatedTransaction(
        NewGroupFeedPayload payload,
        string creatorAddress)
    {
        var signature = new SignatureInfo(
            creatorAddress,
            CreateSignatureString());

        var validatorSignature = new SignatureInfo(
            "validator-address",
            CreateSignatureString());

        var unsignedTx = new UnsignedTransaction<NewGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            NewGroupFeedPayloadHandler.NewGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);

        var signedTx = new SignedTransaction<NewGroupFeedPayload>(unsignedTx, signature);
        return new ValidatedTransaction<NewGroupFeedPayload>(signedTx, validatorSignature);
    }

    private static string CreateSignatureString()
    {
        var bytes = new byte[64];
        Random.NextBytes(bytes);
        return Convert.ToHexString(bytes).ToLower();
    }
}
