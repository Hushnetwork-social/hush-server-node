using FluentAssertions;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

public class AddMembersToCustomCircleTransactionHandlerTests
{
    [Fact]
    public async Task HandleAddMembersToCustomCircleTransactionAsync_WithValidFollowedMember_ShouldPersistMembershipAndRotateKey()
    {
        var mocker = new AutoMocker();

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "Friends", "", false, new BlockIndex(1), 4, IsInnerCircle: false, OwnerPublicAddress: owner));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.OwnerHasChatFeedWithMemberAsync(owner, member))
            .ReturnsAsync(true);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, member))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerRotationAsync(
                feedId,
                RotationTrigger.Join,
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<IReadOnlyCollection<string>?>()))
            .ReturnsAsync(KeyRotationResult.Success(5, new GroupFeedKeyRotationPayload(
                feedId,
                NewKeyGeneration: 5,
                PreviousKeyGeneration: 4,
                ValidFromBlock: 100,
                new[]
                {
                    new GroupFeedEncryptedKey(owner, "owner-enc"),
                    new GroupFeedEncryptedKey(member, "member-enc")
                },
                RotationTrigger.Join)));

        GroupFeedKeyGenerationEntity? capturedKeyGeneration = null;
        IReadOnlyList<GroupFeedParticipantEntity>? capturedParticipantsToAdd = null;
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.ApplyInnerCircleMembershipAndKeyRotationAsync(
                feedId,
                It.IsAny<IReadOnlyList<GroupFeedParticipantEntity>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<GroupFeedKeyGenerationEntity>(),
                It.IsAny<BlockIndex>()))
            .Callback<FeedId, IReadOnlyList<GroupFeedParticipantEntity>, IReadOnlyList<string>, BlockIndex, GroupFeedKeyGenerationEntity, BlockIndex>(
                (_, participants, _, _, keyGeneration, _) =>
                {
                    capturedParticipantsToAdd = participants;
                    capturedKeyGeneration = keyGeneration;
                })
            .Returns(Task.CompletedTask);

        var handler = mocker.CreateInstance<AddMembersToCustomCircleTransactionHandler>();
        var payload = new AddMembersToCustomCirclePayload(feedId, owner, new[] { new CustomCircleMember(member, "enc") });
        var validTx = BuildValidated(payload, owner);

        await handler.HandleAddMembersToCustomCircleTransactionAsync(validTx);

        capturedParticipantsToAdd.Should().NotBeNull();
        capturedParticipantsToAdd!.Should().ContainSingle(p => p.ParticipantPublicAddress == member);

        capturedKeyGeneration.Should().NotBeNull();
        capturedKeyGeneration!.KeyGeneration.Should().Be(5);
        capturedKeyGeneration.EncryptedKeys.Should().HaveCount(2);

        mocker.GetMock<IFeedParticipantsCacheService>()
            .Verify(x => x.InvalidateKeyGenerationsAsync(feedId), Times.Once);
        mocker.GetMock<IGroupMembersCacheService>()
            .Verify(x => x.InvalidateGroupMembersAsync(feedId), Times.Once);
    }

    [Fact]
    public async Task HandleAddMembersToCustomCircleTransactionAsync_WhenMemberIsNotFollowed_ShouldThrow()
    {
        var mocker = new AutoMocker();

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "Friends", "", false, new BlockIndex(1), 0, IsInnerCircle: false, OwnerPublicAddress: owner));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.OwnerHasChatFeedWithMemberAsync(owner, member))
            .ReturnsAsync(false);

        var handler = mocker.CreateInstance<AddMembersToCustomCircleTransactionHandler>();
        var payload = new AddMembersToCustomCirclePayload(feedId, owner, new[] { new CustomCircleMember(member, "enc") });
        var validTx = BuildValidated(payload, owner);

        var act = () => handler.HandleAddMembersToCustomCircleTransactionAsync(validTx);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("CUSTOM_CIRCLE_MEMBER_NOT_FOLLOWED");

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.ApplyInnerCircleMembershipAndKeyRotationAsync(
                It.IsAny<FeedId>(),
                It.IsAny<IReadOnlyList<GroupFeedParticipantEntity>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<GroupFeedKeyGenerationEntity>(),
                It.IsAny<BlockIndex>()), Times.Never);
    }

    private static ValidatedTransaction<AddMembersToCustomCirclePayload> BuildValidated(
        AddMembersToCustomCirclePayload payload,
        string signatory)
    {
        var unsignedTx = new UnsignedTransaction<AddMembersToCustomCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            AddMembersToCustomCirclePayloadHandler.AddMembersToCustomCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            100);

        var signed = new SignedTransaction<AddMembersToCustomCirclePayload>(
            unsignedTx,
            new SignatureInfo(signatory, "sig"));

        return new ValidatedTransaction<AddMembersToCustomCirclePayload>(
            signed,
            new SignatureInfo("validator", "sig"));
    }
}
