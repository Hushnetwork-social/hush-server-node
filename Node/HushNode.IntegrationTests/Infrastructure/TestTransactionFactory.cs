using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using HushServerNode.Testing;
using Olimpo;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// Factory for creating signed transactions for integration tests.
/// </summary>
internal static class TestTransactionFactory
{
    /// <summary>
    /// Creates a signed identity registration transaction.
    /// </summary>
    /// <param name="identity">The test identity to register.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateIdentityRegistration(TestIdentity identity)
    {
        var payload = new FullIdentityPayload(
            identity.DisplayName,
            identity.PublicSigningAddress,
            identity.PublicEncryptAddress,
            IsPublic: false);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            FullIdentityPayloadHandler.FullIdentityPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            identity.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<FullIdentityPayload>(
            unsignedTransaction,
            new SignatureInfo(identity.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// Creates a signed chat feed creation transaction.
    /// </summary>
    /// <param name="initiator">The identity initiating the chat.</param>
    /// <param name="recipient">The identity being invited to chat.</param>
    /// <returns>Tuple of (JSON transaction, FeedId, AES key) for later message encryption.</returns>
    public static (string Transaction, FeedId FeedId, string AesKey) CreateChatFeed(
        TestIdentity initiator,
        TestIdentity recipient)
    {
        var feedId = FeedId.NewFeedId;
        var aesKey = EncryptKeys.GenerateAesKey();

        // Encrypt the AES key for each participant using their public encrypt keys
        var initiatorEncryptedKey = EncryptKeys.Encrypt(aesKey, initiator.PublicEncryptAddress);
        var recipientEncryptedKey = EncryptKeys.Encrypt(aesKey, recipient.PublicEncryptAddress);

        var initiatorParticipant = new ChatFeedParticipant(
            feedId,
            initiator.PublicSigningAddress,
            initiatorEncryptedKey);

        var recipientParticipant = new ChatFeedParticipant(
            feedId,
            recipient.PublicSigningAddress,
            recipientEncryptedKey);

        var payload = new NewChatFeedPayload(
            feedId,
            FeedType.Chat,
            [initiatorParticipant, recipientParticipant]);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewChatFeedPayloadHandler.NewChatFeedPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            initiator.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewChatFeedPayload>(
            unsignedTransaction,
            new SignatureInfo(initiator.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), feedId, aesKey);
    }

    /// <summary>
    /// Creates a signed feed message transaction.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The feed to send the message to.</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the feed.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateFeedMessage(
        TestIdentity sender,
        FeedId feedId,
        string message,
        string feedAesKey)
    {
        var messageId = FeedMessageId.NewFeedMessageId;
        var encryptedContent = EncryptKeys.AesEncrypt(message, feedAesKey);

        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewFeedMessagePayloadHandler.NewFeedMessagePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            sender.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewFeedMessagePayload>(
            unsignedTransaction,
            new SignatureInfo(sender.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }
}
