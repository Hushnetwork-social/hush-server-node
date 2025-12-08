using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Feeds.Model;

public static class NewPersonalFeedPayloadHandler
{
    public static Guid NewPersonalFeedPayloadKind { get; } = Guid.Parse("70c718a9-14d0-4b70-ad37-fd8bfe184386");

    /// <summary>
    /// Creates a new personal feed transaction.
    /// </summary>
    /// <param name="encryptedFeedKey">The feed's AES key encrypted with the owner's RSA public key</param>
    public static UnsignedTransaction<NewPersonalFeedPayload> CreateNewPersonalFeedTransaction(string encryptedFeedKey) =>
        UnsignedTransactionHandler.CreateNew(
            NewPersonalFeedPayloadKind,
            Timestamp.Current,
            new NewPersonalFeedPayload(FeedId.NewFeedId, string.Empty, FeedType.Personal, encryptedFeedKey));
}