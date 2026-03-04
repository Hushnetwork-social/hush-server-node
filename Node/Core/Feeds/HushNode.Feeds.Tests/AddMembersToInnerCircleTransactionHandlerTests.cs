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

public class AddMembersToInnerCircleTransactionHandlerTests
{
    [Fact]
    public async Task HandleAddMembersToInnerCircleTransactionAsync_WhenRotationFails_ShouldNotPersistMembership()
    {
        var mocker = new AutoMocker();

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        mocker.GetMock<IBlockchainCache>()
            .Setup(x => x.LastBlockIndex)
            .Returns(new BlockIndex(100));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(new GroupFeed(feedId, "Inner Circle", "", false, new BlockIndex(1), 0, IsInnerCircle: true, OwnerPublicAddress: owner));

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, member))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        mocker.GetMock<IKeyRotationService>()
            .Setup(x => x.TriggerRotationAsync(feedId, RotationTrigger.Join, It.IsAny<IReadOnlyCollection<string>?>(), It.IsAny<IReadOnlyCollection<string>?>()))
            .ReturnsAsync(KeyRotationResult.Failure("rotation failed"));

        var handler = mocker.CreateInstance<AddMembersToInnerCircleTransactionHandler>();
        var payload = new AddMembersToInnerCirclePayload(owner, new[] { new InnerCircleMember(member, "enc") });
        var validTx = BuildValidated(payload, owner);

        var act = () => handler.HandleAddMembersToInnerCircleTransactionAsync(validTx);

        await act.Should().ThrowAsync<InvalidOperationException>();

        mocker.GetMock<IFeedsStorageService>()
            .Verify(x => x.ApplyInnerCircleMembershipAndKeyRotationAsync(
                It.IsAny<FeedId>(),
                It.IsAny<IReadOnlyList<GroupFeedParticipantEntity>>(),
                It.IsAny<IReadOnlyList<string>>(),
                It.IsAny<BlockIndex>(),
                It.IsAny<GroupFeedKeyGenerationEntity>(),
                It.IsAny<BlockIndex>()), Times.Never);
    }

    private static ValidatedTransaction<AddMembersToInnerCirclePayload> BuildValidated(
        AddMembersToInnerCirclePayload payload,
        string signatory)
    {
        var unsignedTx = new UnsignedTransaction<AddMembersToInnerCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            AddMembersToInnerCirclePayloadHandler.AddMembersToInnerCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            100);

        var signed = new SignedTransaction<AddMembersToInnerCirclePayload>(
            unsignedTx,
            new SignatureInfo(signatory, "sig"));

        return new ValidatedTransaction<AddMembersToInnerCirclePayload>(
            signed,
            new SignatureInfo("validator", "sig"));
    }
}
