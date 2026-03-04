using FluentAssertions;
using HushNode.Feeds.Tests.Fixtures;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

public class AddMembersToCustomCircleContentHandlerTests
{
    [Fact]
    public void ValidateAndSign_WithValidMembers_ShouldSucceed()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var payload = new AddMembersToCustomCirclePayload(
            feedId,
            owner,
            new[] { new CustomCircleMember(member, "member-encrypt") });

        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "Friends", "", false, new HushShared.Blockchain.BlockModel.BlockIndex(1), 0, IsInnerCircle: false, OwnerPublicAddress: owner));
        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasChatFeedWithMemberAsync(owner, member))
            .ReturnsAsync(true);
        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, member))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(member))
            .ReturnsAsync(new Profile("Member", "member", member, "member-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        var handler = mocker.CreateInstance<AddMembersToCustomCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WhenMemberIsNotFollowed_ShouldReturnNull()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();

        var payload = new AddMembersToCustomCirclePayload(
            feedId,
            owner,
            new[] { new CustomCircleMember(member, "member-encrypt") });

        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetGroupFeedAsync(feedId))
            .ReturnsAsync(new GroupFeed(feedId, "Friends", "", false, new HushShared.Blockchain.BlockModel.BlockIndex(1), 0, IsInnerCircle: false, OwnerPublicAddress: owner));
        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.OwnerHasChatFeedWithMemberAsync(owner, member))
            .ReturnsAsync(false);

        var handler = mocker.CreateInstance<AddMembersToCustomCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    private static SignedTransaction<AddMembersToCustomCirclePayload> CreateSigned(AddMembersToCustomCirclePayload payload, string signatory)
    {
        var unsigned = new UnsignedTransaction<AddMembersToCustomCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            AddMembersToCustomCirclePayloadHandler.AddMembersToCustomCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            200);

        return new SignedTransaction<AddMembersToCustomCirclePayload>(
            unsigned,
            new SignatureInfo(signatory, "sig"));
    }
}
