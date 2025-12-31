using HushShared.Blockchain.BlockModel;
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

    #region Admin Control Payloads

    public static BlockMemberPayload CreateBlockMemberPayload(
        FeedId feedId,
        string adminAddress,
        string targetAddress,
        string? reason = null)
    {
        return new BlockMemberPayload(feedId, adminAddress, targetAddress, reason);
    }

    public static SignedTransaction<BlockMemberPayload> CreateBlockMemberSignedTransaction(
        BlockMemberPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<BlockMemberPayload>(
            new TransactionId(Guid.NewGuid()),
            BlockMemberPayloadHandler.BlockMemberPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<BlockMemberPayload>(unsignedTx, signature);
    }

    public static UnblockMemberPayload CreateUnblockMemberPayload(
        FeedId feedId,
        string adminAddress,
        string targetAddress)
    {
        return new UnblockMemberPayload(feedId, adminAddress, targetAddress);
    }

    public static SignedTransaction<UnblockMemberPayload> CreateUnblockMemberSignedTransaction(
        UnblockMemberPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<UnblockMemberPayload>(
            new TransactionId(Guid.NewGuid()),
            UnblockMemberPayloadHandler.UnblockMemberPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<UnblockMemberPayload>(unsignedTx, signature);
    }

    public static PromoteToAdminPayload CreatePromoteToAdminPayload(
        FeedId feedId,
        string adminAddress,
        string targetAddress)
    {
        return new PromoteToAdminPayload(feedId, adminAddress, targetAddress);
    }

    public static SignedTransaction<PromoteToAdminPayload> CreatePromoteToAdminSignedTransaction(
        PromoteToAdminPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<PromoteToAdminPayload>(
            new TransactionId(Guid.NewGuid()),
            PromoteToAdminPayloadHandler.PromoteToAdminPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<PromoteToAdminPayload>(unsignedTx, signature);
    }

    public static UpdateGroupFeedTitlePayload CreateUpdateTitlePayload(
        FeedId feedId,
        string adminAddress,
        string newTitle)
    {
        return new UpdateGroupFeedTitlePayload(feedId, adminAddress, newTitle);
    }

    public static SignedTransaction<UpdateGroupFeedTitlePayload> CreateUpdateTitleSignedTransaction(
        UpdateGroupFeedTitlePayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<UpdateGroupFeedTitlePayload>(
            new TransactionId(Guid.NewGuid()),
            UpdateGroupFeedTitlePayloadHandler.UpdateGroupFeedTitlePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<UpdateGroupFeedTitlePayload>(unsignedTx, signature);
    }

    public static UpdateGroupFeedDescriptionPayload CreateUpdateDescriptionPayload(
        FeedId feedId,
        string adminAddress,
        string newDescription)
    {
        return new UpdateGroupFeedDescriptionPayload(feedId, adminAddress, newDescription);
    }

    public static SignedTransaction<UpdateGroupFeedDescriptionPayload> CreateUpdateDescriptionSignedTransaction(
        UpdateGroupFeedDescriptionPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<UpdateGroupFeedDescriptionPayload>(
            new TransactionId(Guid.NewGuid()),
            UpdateGroupFeedDescriptionPayloadHandler.UpdateGroupFeedDescriptionPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<UpdateGroupFeedDescriptionPayload>(unsignedTx, signature);
    }

    public static DeleteGroupFeedPayload CreateDeleteGroupFeedPayload(
        FeedId feedId,
        string adminAddress)
    {
        return new DeleteGroupFeedPayload(feedId, adminAddress);
    }

    public static SignedTransaction<DeleteGroupFeedPayload> CreateDeleteGroupFeedSignedTransaction(
        DeleteGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<DeleteGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            DeleteGroupFeedPayloadHandler.DeleteGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<DeleteGroupFeedPayload>(unsignedTx, signature);
    }

    public static GroupFeed CreateGroupFeed(
        FeedId feedId,
        string title = "Test Group",
        string description = "Test Description",
        bool isDeleted = false)
    {
        return new GroupFeed(feedId, title, description, false, new BlockIndex(100), 0)
        {
            IsDeleted = isDeleted
        };
    }

    public static GroupFeedParticipantEntity CreateParticipantEntity(
        FeedId feedId,
        string address,
        ParticipantType participantType)
    {
        return new GroupFeedParticipantEntity(
            feedId,
            address,
            participantType,
            new BlockIndex(100));
    }

    #endregion

    #region FEAT-008: Join/Leave Mechanics Payloads

    public static JoinGroupFeedPayload CreateJoinGroupFeedPayload(
        FeedId feedId,
        string joiningUserAddress,
        string? invitationSignature = null)
    {
        return new JoinGroupFeedPayload(feedId, joiningUserAddress, invitationSignature);
    }

    public static SignedTransaction<JoinGroupFeedPayload> CreateJoinGroupFeedSignedTransaction(
        JoinGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<JoinGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            JoinGroupFeedPayloadHandler.JoinGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<JoinGroupFeedPayload>(unsignedTx, signature);
    }

    public static ValidatedTransaction<JoinGroupFeedPayload> CreateJoinGroupFeedValidatedTransaction(
        JoinGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var validatorSignature = new SignatureInfo("validator-address", CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<JoinGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            JoinGroupFeedPayloadHandler.JoinGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        var signedTx = new SignedTransaction<JoinGroupFeedPayload>(unsignedTx, signature);
        return new ValidatedTransaction<JoinGroupFeedPayload>(signedTx, validatorSignature);
    }

    public static AddMemberToGroupFeedPayload CreateAddMemberToGroupFeedPayload(
        FeedId feedId,
        string adminAddress,
        string newMemberAddress,
        string encryptedKey)
    {
        return new AddMemberToGroupFeedPayload(feedId, adminAddress, newMemberAddress, encryptedKey);
    }

    public static SignedTransaction<AddMemberToGroupFeedPayload> CreateAddMemberToGroupFeedSignedTransaction(
        AddMemberToGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<AddMemberToGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            AddMemberToGroupFeedPayloadHandler.AddMemberToGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<AddMemberToGroupFeedPayload>(unsignedTx, signature);
    }

    public static ValidatedTransaction<AddMemberToGroupFeedPayload> CreateAddMemberToGroupFeedValidatedTransaction(
        AddMemberToGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var validatorSignature = new SignatureInfo("validator-address", CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<AddMemberToGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            AddMemberToGroupFeedPayloadHandler.AddMemberToGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        var signedTx = new SignedTransaction<AddMemberToGroupFeedPayload>(unsignedTx, signature);
        return new ValidatedTransaction<AddMemberToGroupFeedPayload>(signedTx, validatorSignature);
    }

    public static LeaveGroupFeedPayload CreateLeaveGroupFeedPayload(
        FeedId feedId,
        string leavingUserAddress)
    {
        return new LeaveGroupFeedPayload(feedId, leavingUserAddress);
    }

    public static SignedTransaction<LeaveGroupFeedPayload> CreateLeaveGroupFeedSignedTransaction(
        LeaveGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<LeaveGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            LeaveGroupFeedPayloadHandler.LeaveGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<LeaveGroupFeedPayload>(unsignedTx, signature);
    }

    public static ValidatedTransaction<LeaveGroupFeedPayload> CreateLeaveGroupFeedValidatedTransaction(
        LeaveGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var validatorSignature = new SignatureInfo("validator-address", CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<LeaveGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            LeaveGroupFeedPayloadHandler.LeaveGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        var signedTx = new SignedTransaction<LeaveGroupFeedPayload>(unsignedTx, signature);
        return new ValidatedTransaction<LeaveGroupFeedPayload>(signedTx, validatorSignature);
    }

    public static GroupFeedParticipantEntity CreateParticipantEntityWithHistory(
        FeedId feedId,
        string address,
        ParticipantType participantType,
        BlockIndex? leftAtBlock = null,
        BlockIndex? lastLeaveBlock = null)
    {
        return new GroupFeedParticipantEntity(
            feedId,
            address,
            participantType,
            new BlockIndex(100))
        {
            LeftAtBlock = leftAtBlock,
            LastLeaveBlock = lastLeaveBlock
        };
    }

    public static GroupFeed CreatePublicGroupFeed(
        FeedId feedId,
        string title = "Test Public Group",
        string description = "Test Description",
        bool isDeleted = false)
    {
        return new GroupFeed(feedId, title, description, true, new BlockIndex(100), 0)
        {
            IsDeleted = isDeleted
        };
    }

    #endregion

    #region FEAT-015: Ban/Unban System Payloads

    public static BanFromGroupFeedPayload CreateBanFromGroupFeedPayload(
        FeedId feedId,
        string adminAddress,
        string bannedUserAddress,
        string? reason = null)
    {
        return new BanFromGroupFeedPayload(feedId, adminAddress, bannedUserAddress, reason);
    }

    public static SignedTransaction<BanFromGroupFeedPayload> CreateBanFromGroupFeedSignedTransaction(
        BanFromGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<BanFromGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            BanFromGroupFeedPayloadHandler.BanFromGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<BanFromGroupFeedPayload>(unsignedTx, signature);
    }

    public static ValidatedTransaction<BanFromGroupFeedPayload> CreateBanFromGroupFeedValidatedTransaction(
        BanFromGroupFeedPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var validatorSignature = new SignatureInfo("validator-address", CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<BanFromGroupFeedPayload>(
            new TransactionId(Guid.NewGuid()),
            BanFromGroupFeedPayloadHandler.BanFromGroupFeedPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        var signedTx = new SignedTransaction<BanFromGroupFeedPayload>(unsignedTx, signature);
        return new ValidatedTransaction<BanFromGroupFeedPayload>(signedTx, validatorSignature);
    }

    #endregion

    #region Key Rotation Payloads

    public static GroupFeedKeyRotationPayload CreateKeyRotationPayload(
        FeedId feedId,
        int newKeyGeneration = 2,
        int previousKeyGeneration = 1,
        long validFromBlock = 100,
        int memberCount = 3)
    {
        var encryptedKeys = Enumerable.Range(0, memberCount)
            .Select(_ => new GroupFeedEncryptedKey(
                CreateAddress(),
                Convert.ToBase64String(new byte[128])))
            .ToArray();

        return new GroupFeedKeyRotationPayload(
            feedId,
            newKeyGeneration,
            previousKeyGeneration,
            validFromBlock,
            encryptedKeys,
            RotationTrigger.Join);
    }

    public static SignedTransaction<GroupFeedKeyRotationPayload> CreateKeyRotationSignedTransaction(
        GroupFeedKeyRotationPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<GroupFeedKeyRotationPayload>(
            new TransactionId(Guid.NewGuid()),
            GroupFeedKeyRotationPayloadHandler.GroupFeedKeyRotationPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        return new SignedTransaction<GroupFeedKeyRotationPayload>(unsignedTx, signature);
    }

    public static ValidatedTransaction<GroupFeedKeyRotationPayload> CreateKeyRotationValidatedTransaction(
        GroupFeedKeyRotationPayload payload,
        string senderAddress)
    {
        var signature = new SignatureInfo(senderAddress, CreateSignatureString());
        var validatorSignature = new SignatureInfo("validator-address", CreateSignatureString());
        var unsignedTx = new UnsignedTransaction<GroupFeedKeyRotationPayload>(
            new TransactionId(Guid.NewGuid()),
            GroupFeedKeyRotationPayloadHandler.GroupFeedKeyRotationPayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            1000);
        var signedTx = new SignedTransaction<GroupFeedKeyRotationPayload>(unsignedTx, signature);
        return new ValidatedTransaction<GroupFeedKeyRotationPayload>(signedTx, validatorSignature);
    }

    #endregion
}
