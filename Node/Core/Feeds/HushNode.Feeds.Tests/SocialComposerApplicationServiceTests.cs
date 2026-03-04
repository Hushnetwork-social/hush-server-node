using FluentAssertions;
using HushNetwork.proto;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Moq;
using Xunit;

namespace HushNode.Feeds.Tests;

public class SocialComposerApplicationServiceTests
{
    [Fact]
    public async Task GetSocialComposerContractAsync_PrivatePreferred_WithInnerCircle_ShouldSelectAndLockIt()
    {
        const string owner = "owner-address";
        var innerCircleId = Guid.NewGuid();
        var customCircleId = Guid.NewGuid();

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        feedsStorage
            .Setup(x => x.GetCirclesForOwnerAsync(owner))
            .ReturnsAsync(new List<CustomCircleSummary>
            {
                new(new FeedId(innerCircleId), "Inner Circle", true, 3, 1, new BlockIndex(1), new BlockIndex(2)),
                new(new FeedId(customCircleId), "Friends", false, 2, 0, new BlockIndex(1), new BlockIndex(2))
            });

        var sut = new SocialComposerApplicationService(feedsStorage.Object);

        var response = await sut.GetSocialComposerContractAsync(new GetSocialComposerContractRequest
        {
            OwnerPublicAddress = owner,
            PreferredVisibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate
        });

        response.Success.Should().BeTrue();
        response.DefaultVisibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityPrivate);
        response.SelectedCircleFeedIds.Should().ContainSingle(x => x == innerCircleId.ToString());
        response.CanSubmit.Should().BeTrue();

        var inner = response.AvailableCircles.Single(x => x.FeedId == innerCircleId.ToString());
        inner.IsSelectedByDefault.Should().BeTrue();
        inner.IsRemovable.Should().BeFalse("last private circle must be locked");

        var custom = response.AvailableCircles.Single(x => x.FeedId == customCircleId.ToString());
        custom.IsSelectedByDefault.Should().BeFalse();
        custom.IsRemovable.Should().BeTrue();
    }

    [Fact]
    public async Task GetSocialComposerContractAsync_PrivatePreferred_WithoutInnerCircle_ShouldBlockSubmit()
    {
        const string owner = "owner-address";

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        feedsStorage
            .Setup(x => x.GetCirclesForOwnerAsync(owner))
            .ReturnsAsync(Array.Empty<CustomCircleSummary>());

        var sut = new SocialComposerApplicationService(feedsStorage.Object);

        var response = await sut.GetSocialComposerContractAsync(new GetSocialComposerContractRequest
        {
            OwnerPublicAddress = owner,
            PreferredVisibility = SocialPostVisibilityProto.SocialPostVisibilityPrivate
        });

        response.Success.Should().BeTrue();
        response.DefaultVisibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityPrivate);
        response.CanSubmit.Should().BeFalse();
        response.ErrorCode.Should().Be("SOCIAL_POST_PRIVATE_REQUIRES_CIRCLE");
        response.SelectedCircleFeedIds.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSocialComposerContractAsync_OpenPreferred_ShouldAllowSubmitWithoutSelectedCircles()
    {
        const string owner = "owner-address";
        var innerCircleId = Guid.NewGuid();

        var feedsStorage = new Mock<IFeedsStorageService>(MockBehavior.Strict);
        feedsStorage
            .Setup(x => x.GetCirclesForOwnerAsync(owner))
            .ReturnsAsync(new List<CustomCircleSummary>
            {
                new(new FeedId(innerCircleId), "Inner Circle", true, 1, 0, new BlockIndex(1), new BlockIndex(1))
            });

        var sut = new SocialComposerApplicationService(feedsStorage.Object);

        var response = await sut.GetSocialComposerContractAsync(new GetSocialComposerContractRequest
        {
            OwnerPublicAddress = owner,
            PreferredVisibility = SocialPostVisibilityProto.SocialPostVisibilityOpen
        });

        response.Success.Should().BeTrue();
        response.DefaultVisibility.Should().Be(SocialPostVisibilityProto.SocialPostVisibilityOpen);
        response.SelectedCircleFeedIds.Should().BeEmpty();
        response.CanSubmit.Should().BeTrue();
    }
}
