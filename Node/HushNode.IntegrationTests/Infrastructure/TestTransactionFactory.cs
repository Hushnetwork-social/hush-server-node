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
    /// Creates a signed personal feed creation transaction.
    /// This must be submitted AFTER identity registration, in the same or subsequent block.
    /// </summary>
    /// <param name="identity">The identity to create a personal feed for.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreatePersonalFeed(TestIdentity identity)
    {
        var (transaction, _) = CreatePersonalFeedWithKey(identity);
        return transaction;
    }

    /// <summary>
    /// Creates a signed personal feed creation transaction and returns the AES key.
    /// This must be submitted AFTER identity registration, in the same or subsequent block.
    /// </summary>
    /// <param name="identity">The identity to create a personal feed for.</param>
    /// <returns>Tuple of (JSON transaction, AES key) for later message encryption.</returns>
    public static (string Transaction, string AesKey) CreatePersonalFeedWithKey(TestIdentity identity)
    {
        // Generate AES key for the feed and encrypt it with the owner's public encryption key
        var feedAesKey = EncryptKeys.GenerateAesKey();
        var encryptedFeedKey = EncryptKeys.Encrypt(feedAesKey, identity.PublicEncryptAddress);

        var unsignedTransaction = NewPersonalFeedPayloadHandler.CreateNewPersonalFeedTransaction(encryptedFeedKey);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            identity.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewPersonalFeedPayload>(
            unsignedTransaction,
            new SignatureInfo(identity.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), feedAesKey);
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
    /// Creates a signed group feed creation transaction.
    /// </summary>
    /// <param name="creator">The identity creating the group.</param>
    /// <param name="groupName">The name of the group.</param>
    /// <param name="isPublic">Whether the group is public (anyone can join).</param>
    /// <returns>Tuple of (JSON transaction, FeedId, AES key) for later message encryption.</returns>
    public static (string Transaction, FeedId FeedId, string AesKey) CreateGroupFeed(
        TestIdentity creator,
        string groupName,
        bool isPublic = false)
    {
        var feedId = FeedId.NewFeedId;
        var aesKey = EncryptKeys.GenerateAesKey();

        // Encrypt the AES key for the creator using their public encrypt key
        var creatorEncryptedKey = EncryptKeys.Encrypt(aesKey, creator.PublicEncryptAddress);

        var creatorParticipant = new GroupFeedParticipant(
            feedId,
            creator.PublicSigningAddress,
            ParticipantType.Owner,
            creatorEncryptedKey,
            KeyGeneration: 1);

        var payload = new NewGroupFeedPayload(
            feedId,
            groupName,
            Description: $"Test group: {groupName}",
            IsPublic: isPublic,
            [creatorParticipant]);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewGroupFeedPayloadHandler.NewGroupFeedPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            creator.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewGroupFeedPayload>(
            unsignedTransaction,
            new SignatureInfo(creator.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), feedId, aesKey);
    }

    /// <summary>
    /// Creates a signed feed message transaction for Personal or Chat feeds.
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

    /// <summary>
    /// Creates a signed group feed message transaction with explicit KeyGeneration.
    /// Use this for Group feeds to properly track which key was used for encryption.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The group feed to send the message to.</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the current key generation.</param>
    /// <param name="keyGeneration">The key generation number (0-based) used for encryption.</param>
    /// <returns>Tuple of (JSON transaction, FeedMessageId) for verification.</returns>
    public static (string Transaction, FeedMessageId MessageId) CreateGroupFeedMessage(
        TestIdentity sender,
        FeedId feedId,
        string message,
        string feedAesKey,
        int keyGeneration)
    {
        var messageId = FeedMessageId.NewFeedMessageId;
        var encryptedContent = EncryptKeys.AesEncrypt(message, feedAesKey);

        var payload = new NewGroupFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            keyGeneration);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            NewGroupFeedMessagePayloadHandler.NewGroupFeedMessagePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            sender.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewGroupFeedMessagePayload>(
            unsignedTransaction,
            new SignatureInfo(sender.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), messageId);
    }
}
