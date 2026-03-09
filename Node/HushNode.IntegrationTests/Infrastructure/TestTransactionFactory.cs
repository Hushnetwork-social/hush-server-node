using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using HushShared.Reactions.Model;
using HushNode.Reactions.Crypto;
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

        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            KeyGeneration: keyGeneration);

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

        return (signedTransaction.ToJson(), messageId);
    }

    /// <summary>
    /// FEAT-057: Creates a signed feed message transaction with a SPECIFIC message ID.
    /// Use this for idempotency testing - submit the same transaction multiple times to test duplicate handling.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The feed to send the message to.</param>
    /// <param name="messageId">The specific message ID to use (allows re-submission testing).</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the feed.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateFeedMessageWithId(
        TestIdentity sender,
        FeedId feedId,
        FeedMessageId messageId,
        string message,
        string feedAesKey)
    {
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
    /// FEAT-066: Creates a signed feed message transaction WITH attachment metadata.
    /// Returns the transaction JSON, message ID, and attachment references for verification.
    /// </summary>
    /// <param name="sender">The identity sending the message.</param>
    /// <param name="feedId">The feed to send the message to.</param>
    /// <param name="message">The plaintext message content.</param>
    /// <param name="feedAesKey">The AES key for the feed.</param>
    /// <param name="attachments">List of attachment references to include in the payload.</param>
    /// <returns>Tuple of (JSON transaction, FeedMessageId) for verification.</returns>
    public static (string Transaction, FeedMessageId MessageId) CreateFeedMessageWithAttachments(
        TestIdentity sender,
        FeedId feedId,
        string message,
        string feedAesKey,
        List<AttachmentReference> attachments)
    {
        var messageId = FeedMessageId.NewFeedMessageId;
        var encryptedContent = EncryptKeys.AesEncrypt(message, feedAesKey);

        var payload = new NewFeedMessagePayload(
            messageId,
            feedId,
            encryptedContent,
            Attachments: attachments);

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

        return (signedTransaction.ToJson(), messageId);
    }

    /// <summary>
    /// Creates a signed identity update transaction (display name change).
    /// </summary>
    public static string CreateIdentityUpdate(TestIdentity identity, string newAlias)
    {
        var unsignedTransaction = UpdateIdentityPayloadHandler.CreateNew(newAlias);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            identity.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<UpdateIdentityPayload>(
            unsignedTransaction,
            new SignatureInfo(identity.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    /// <summary>
    /// Creates a signed social post creation transaction (FEAT-086).
    /// </summary>
    public static (string Transaction, Guid PostId) CreateSocialPost(
        TestIdentity author,
        string content,
        SocialPostVisibility visibility,
        IReadOnlyCollection<FeedId>? circleFeedIds = null,
        IReadOnlyCollection<SocialPostAttachment>? attachments = null)
    {
        var postId = Guid.NewGuid();
        var audience = new SocialPostAudience(
            visibility,
            (circleFeedIds ?? Array.Empty<FeedId>()).Select(x => x.ToString()).ToArray());

        var payload = new CreateSocialPostPayload(
            postId,
            postId,
            author.PublicSigningAddress,
            null,
            content,
            audience,
            (attachments ?? Array.Empty<SocialPostAttachment>()).ToArray(),
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            CreateSocialPostPayloadHandler.CreateSocialPostPayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            author.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<CreateSocialPostPayload>(
            unsignedTransaction,
            new SignatureInfo(author.PublicSigningAddress, signature));

        return (signedTransaction.ToJson(), postId);
    }

    /// <summary>
    /// Creates a signed dev-mode reaction transaction for integration tests.
    /// The payload uses a simple one-hot point encoding so tally deltas remain easy to assert.
    /// </summary>
    public static string CreateDevModeReaction(
        TestIdentity reactor,
        FeedId reactionScopeId,
        FeedMessageId messageId,
        byte[] nullifier,
        int emojiIndex)
    {
        if (emojiIndex < 0 || emojiIndex > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(emojiIndex), "emojiIndex must be between 0 and 5.");
        }

        if (nullifier.Length != 32)
        {
            throw new ArgumentException("Nullifier must be exactly 32 bytes.", nameof(nullifier));
        }

        var curve = new BabyJubJubCurve();
        var generatorX = PadTo32Bytes(curve.Generator.X.ToByteArray(isUnsigned: true, isBigEndian: true));
        var generatorY = PadTo32Bytes(curve.Generator.Y.ToByteArray(isUnsigned: true, isBigEndian: true));
        var identityX = PadTo32Bytes(curve.Identity.X.ToByteArray(isUnsigned: true, isBigEndian: true));
        var identityY = PadTo32Bytes(curve.Identity.Y.ToByteArray(isUnsigned: true, isBigEndian: true));

        var ciphertextC1X = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorX : identityX)
            .ToArray();
        var ciphertextC1Y = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorY : identityY)
            .ToArray();
        var ciphertextC2X = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorX : identityX)
            .ToArray();
        var ciphertextC2Y = Enumerable.Range(0, 6)
            .Select(i => i == emojiIndex ? generatorY : identityY)
            .ToArray();

        var payload = new NewReactionPayload(
            reactionScopeId,
            messageId,
            nullifier,
            ciphertextC1X,
            ciphertextC1Y,
            ciphertextC2X,
            ciphertextC2Y,
            new byte[256],
            "dev-mode-v1",
            BuildReactionBackup(emojiIndex));

        var unsignedTransaction = new UnsignedTransaction<NewReactionPayload>(
            new TransactionId(Guid.NewGuid()),
            NewReactionPayloadHandler.NewReactionPayloadKind,
            Timestamp.Current,
            payload,
            1000);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            reactor.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<NewReactionPayload>(
            unsignedTransaction,
            new SignatureInfo(reactor.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }

    private static byte[] BuildReactionBackup(int emojiIndex)
    {
        var backup = new byte[32];
        backup[31] = checked((byte)emojiIndex);
        return backup;
    }

    private static byte[] PadTo32Bytes(byte[] input)
    {
        if (input.Length >= 32)
        {
            return input[^32..];
        }

        var padded = new byte[32];
        Array.Copy(input, 0, padded, 32 - input.Length, input.Length);
        return padded;
    }

    /// <summary>
    /// Creates a signed UpdateGroupFeedTitle transaction.
    /// </summary>
    /// <param name="admin">The admin identity issuing the title change.</param>
    /// <param name="feedId">The group feed to rename.</param>
    /// <param name="newTitle">The new title for the group.</param>
    /// <returns>JSON-serialized signed transaction ready for submission.</returns>
    public static string CreateUpdateGroupFeedTitle(TestIdentity admin, FeedId feedId, string newTitle)
    {
        var payload = new UpdateGroupFeedTitlePayload(
            feedId,
            admin.PublicSigningAddress,
            newTitle);

        var unsignedTransaction = UnsignedTransactionHandler.CreateNew(
            UpdateGroupFeedTitlePayloadHandler.UpdateGroupFeedTitlePayloadKind,
            Timestamp.Current,
            payload);

        var signature = DigitalSignature.SignMessage(
            unsignedTransaction.ToJson(),
            admin.PrivateSigningKey);

        var signedTransaction = new SignedTransaction<UpdateGroupFeedTitlePayload>(
            unsignedTransaction,
            new SignatureInfo(admin.PublicSigningAddress, signature));

        return signedTransaction.ToJson();
    }
}
