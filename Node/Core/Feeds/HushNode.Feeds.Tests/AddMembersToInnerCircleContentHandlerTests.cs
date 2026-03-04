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

public class AddMembersToInnerCircleContentHandlerTests
{
    [Fact]
    public void ValidateAndSign_WithValidMembers_ShouldSucceed()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var feedId = TestDataFactory.CreateFeedId();
        var member = TestDataFactory.CreateAddress();

        var payload = new AddMembersToInnerCirclePayload(
            owner,
            new[] { new InnerCircleMember(member, "member-encrypt") });

        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(new GroupFeed(feedId, "Inner Circle", "", false, new HushShared.Blockchain.BlockModel.BlockIndex(1), 0, IsInnerCircle: true, OwnerPublicAddress: owner));

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetParticipantWithHistoryAsync(feedId, member))
            .ReturnsAsync((GroupFeedParticipantEntity?)null);

        mocker.GetMock<HushNode.Identity.Storage.IIdentityStorageService>()
            .Setup(x => x.RetrieveIdentityAsync(member))
            .ReturnsAsync(new Profile("Member", "member", member, "member-encrypt", true, new HushShared.Blockchain.BlockModel.BlockIndex(1)));

        var handler = mocker.CreateInstance<AddMembersToInnerCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateAndSign_WithDuplicateMembersInPayload_ShouldReturnNull()
    {
        var mocker = new AutoMocker();
        MockServices.ConfigureCredentialsProvider(mocker);

        var owner = TestDataFactory.CreateAddress();
        var member = TestDataFactory.CreateAddress();

        var payload = new AddMembersToInnerCirclePayload(
            owner,
            new[]
            {
                new InnerCircleMember(member, "member-encrypt"),
                new InnerCircleMember(member, "member-encrypt")
            });

        var tx = CreateSigned(payload, owner);

        mocker.GetMock<HushNode.Feeds.Storage.IFeedsStorageService>()
            .Setup(x => x.GetInnerCircleByOwnerAsync(owner))
            .ReturnsAsync(new GroupFeed(TestDataFactory.CreateFeedId(), "Inner Circle", "", false, new HushShared.Blockchain.BlockModel.BlockIndex(1), 0, IsInnerCircle: true, OwnerPublicAddress: owner));

        var handler = mocker.CreateInstance<AddMembersToInnerCircleContentHandler>();

        var result = handler.ValidateAndSign(tx);

        result.Should().BeNull();
    }

    private static SignedTransaction<AddMembersToInnerCirclePayload> CreateSigned(AddMembersToInnerCirclePayload payload, string signatory)
    {
        var unsigned = new UnsignedTransaction<AddMembersToInnerCirclePayload>(
            new TransactionId(Guid.NewGuid()),
            AddMembersToInnerCirclePayloadHandler.AddMembersToInnerCirclePayloadKind,
            new Timestamp(DateTime.UtcNow),
            payload,
            200);

        return new SignedTransaction<AddMembersToInnerCirclePayload>(
            unsigned,
            new SignatureInfo(signatory, "sig"));
    }
}
