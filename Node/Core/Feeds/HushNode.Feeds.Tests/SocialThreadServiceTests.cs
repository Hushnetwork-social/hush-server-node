using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Blockchain.Model;
using HushShared.Feeds.Model;
using HushShared.Reactions.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Xunit;

namespace HushNode.Feeds.Tests;

public class SocialThreadServiceTests
{
    [Fact]
    public async Task AuthorizeAsync_OpenPostWriteForAuthenticatedUser_ShouldAllow()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(CreatePost(postId, SocialPostVisibility.Open));

        var sut = mocker.CreateInstance<SocialThreadService>();

        var result = await sut.AuthorizeAsync(postId, "user-address", true, SocialThreadAccessMode.Write);

        result.IsAllowed.Should().BeTrue();
        result.ErrorCode.Should().Be(SocialThreadAccessErrorCode.None);
    }

    [Fact]
    public async Task AuthorizeAsync_OpenPostWriteForGuest_ShouldRequireAuthentication()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(CreatePost(postId, SocialPostVisibility.Open));

        var sut = mocker.CreateInstance<SocialThreadService>();

        var result = await sut.AuthorizeAsync(postId, null, false, SocialThreadAccessMode.Write);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(SocialThreadAccessErrorCode.AuthenticationRequired);
    }

    [Fact]
    public async Task AuthorizeAsync_PrivatePostForUnauthorizedUser_ShouldDeny()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(CreatePost(postId, SocialPostVisibility.Private));
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.IsUserInAnyActiveCircleAsync("other-user", It.IsAny<IReadOnlyList<FeedId>>()))
            .ReturnsAsync(false);

        var sut = mocker.CreateInstance<SocialThreadService>();

        var result = await sut.AuthorizeAsync(postId, "other-user", true, SocialThreadAccessMode.Write);

        result.IsAllowed.Should().BeFalse();
        result.ErrorCode.Should().Be(SocialThreadAccessErrorCode.AccessDenied);
    }

    [Fact]
    public async Task ResolveThreadEntryAsync_ReplyToReply_ShouldNormalizeToTopLevelRoot()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var rootCommentId = new FeedMessageId(Guid.NewGuid());
        var replyId = new FeedMessageId(Guid.NewGuid());
        var newReplyId = new FeedMessageId(Guid.NewGuid());

        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.GetFeedMessageByIdAsync(replyId))
            .ReturnsAsync(new FeedMessage(
                replyId,
                new FeedId(postId),
                "reply",
                "author",
                new Timestamp(DateTime.UtcNow),
                new BlockIndex(10),
                ReplyToMessageId: rootCommentId));

        var sut = mocker.CreateInstance<SocialThreadService>();

        var result = await sut.ResolveThreadEntryAsync(postId, newReplyId, replyId);

        result.Access.IsAllowed.Should().BeTrue();
        result.ThreadEntry.Should().NotBeNull();
        result.ThreadEntry!.Kind.Should().Be(SocialThreadEntryKind.Reply);
        result.ThreadEntry.ParentCommentId.Should().Be(rootCommentId);
        result.ThreadEntry.ThreadRootId.Should().Be(rootCommentId);
    }

    [Fact]
    public async Task GetCommentsPageAsync_ShouldSortByReactionCountThenNewestFirst()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var olderHighReactionId = new FeedMessageId(Guid.NewGuid());
        var newestSameReactionId = new FeedMessageId(Guid.NewGuid());
        var lowReactionId = new FeedMessageId(Guid.NewGuid());
        var feedId = new FeedId(postId);

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(CreatePost(postId, SocialPostVisibility.Open));
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.RetrieveLastFeedMessagesForFeedAsync(feedId, It.IsAny<BlockIndex>()))
            .ReturnsAsync(new[]
            {
                CreateMessage(postId, olderHighReactionId, DateTime.UtcNow.AddMinutes(-10)),
                CreateMessage(postId, newestSameReactionId, DateTime.UtcNow.AddMinutes(-1)),
                CreateMessage(postId, lowReactionId, DateTime.UtcNow.AddMinutes(-2))
            });
        mocker.GetMock<IReactionService>()
            .Setup(x => x.GetTalliesAsync(feedId, It.IsAny<IEnumerable<FeedMessageId>>()))
            .ReturnsAsync(new[]
            {
                CreateTally(feedId, olderHighReactionId, 3),
                CreateTally(feedId, newestSameReactionId, 3),
                CreateTally(feedId, lowReactionId, 1)
            });

        var sut = mocker.CreateInstance<SocialThreadService>();

        var result = await sut.GetCommentsPageAsync(postId, null, false, null, null);

        result.Success.Should().BeTrue();
        result.Entries.Select(x => x.Message.FeedMessageId).Should().ContainInOrder(
            newestSameReactionId,
            olderHighReactionId,
            lowReactionId);
    }

    [Fact]
    public async Task GetRepliesPageAsync_ShouldUseReplyPagingDefaultAndBeforeCursor()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        var rootCommentId = new FeedMessageId(Guid.NewGuid());
        var feedId = new FeedId(postId);
        var replyIds = Enumerable.Range(0, 7).Select(_ => new FeedMessageId(Guid.NewGuid())).ToArray();

        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(CreatePost(postId, SocialPostVisibility.Open));
        mocker.GetMock<IFeedMessageStorageService>()
            .Setup(x => x.RetrieveLastFeedMessagesForFeedAsync(feedId, It.IsAny<BlockIndex>()))
            .ReturnsAsync(replyIds.Select((id, index) =>
                new FeedMessage(
                    id,
                    feedId,
                    $"reply-{index}",
                    "author",
                    new Timestamp(DateTime.UtcNow.AddMinutes(-index)),
                    new BlockIndex(index + 1),
                    ReplyToMessageId: rootCommentId)));
        mocker.GetMock<IReactionService>()
            .Setup(x => x.GetTalliesAsync(feedId, It.IsAny<IEnumerable<FeedMessageId>>()))
            .ReturnsAsync(replyIds.Select(id => CreateTally(feedId, id, 0)).ToArray());

        var sut = mocker.CreateInstance<SocialThreadService>();

        var firstPage = await sut.GetRepliesPageAsync(postId, rootCommentId, null, false, null, null);
        var secondPage = await sut.GetRepliesPageAsync(postId, rootCommentId, null, false, null, firstPage.Entries.Last().Message.FeedMessageId);

        firstPage.Entries.Should().HaveCount(5);
        firstPage.HasMore.Should().BeTrue();
        secondPage.Entries.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetCommentsPageAsync_PrivateUnauthorizedRead_ShouldReturnDeniedResult()
    {
        var mocker = new AutoMocker();
        var postId = Guid.NewGuid();
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.GetSocialPostAsync(postId))
            .ReturnsAsync(CreatePost(postId, SocialPostVisibility.Private));
        mocker.GetMock<IFeedsStorageService>()
            .Setup(x => x.IsUserInAnyActiveCircleAsync("unauthorized", It.IsAny<IReadOnlyList<FeedId>>()))
            .ReturnsAsync(false);

        var sut = mocker.CreateInstance<SocialThreadService>();

        var result = await sut.GetCommentsPageAsync(postId, "unauthorized", true, null, null);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(SocialThreadAccessErrorCode.AccessDenied);
        result.Entries.Should().BeEmpty();
    }

    private static SocialPostEntity CreatePost(Guid postId, SocialPostVisibility visibility) =>
        new()
        {
            PostId = postId,
            ReactionScopeId = postId,
            AuthorPublicAddress = "author",
            Content = "post",
            AudienceVisibility = visibility,
            AudienceCircles =
            [
                new SocialPostAudienceCircleEntity
                {
                    PostId = postId,
                    CircleFeedId = new FeedId(Guid.NewGuid())
                }
            ]
        };

    private static FeedMessage CreateMessage(Guid postId, FeedMessageId messageId, DateTime timestampUtc) =>
        new(
            messageId,
            new FeedId(postId),
            "comment",
            "author",
            new Timestamp(DateTime.SpecifyKind(timestampUtc, DateTimeKind.Utc)),
            new BlockIndex(1));

    private static MessageReactionTally CreateTally(FeedId feedId, FeedMessageId messageId, int totalCount) =>
        new(
            MessageId: messageId,
            FeedId: feedId,
            TallyC1X: CreateEmptyPointArray(),
            TallyC1Y: CreateEmptyPointArray(),
            TallyC2X: CreateEmptyPointArray(),
            TallyC2Y: CreateEmptyPointArray(),
            TotalCount: totalCount,
            Version: 1,
            LastUpdated: DateTime.UtcNow);

    private static byte[][] CreateEmptyPointArray() =>
        Enumerable.Range(0, 6).Select(_ => new byte[32]).ToArray();
}
