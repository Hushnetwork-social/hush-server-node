using FluentAssertions;
using HushShared.Feeds.Model;
using Xunit;

namespace HushNode.Feeds.Tests;

public class SocialThreadContractsTests
{
    [Fact]
    public void CommentContract_ShouldRepresentTopLevelComment()
    {
        var postId = Guid.NewGuid();
        var commentId = new FeedMessageId(Guid.NewGuid());

        var contract = SocialThreadEntryContract.Comment(postId, commentId);

        contract.PostId.Should().Be(postId);
        contract.EntryId.Should().Be(commentId);
        contract.Kind.Should().Be(SocialThreadEntryKind.Comment);
        contract.ParentCommentId.Should().BeNull();
        contract.ThreadRootId.Should().Be(commentId);
        contract.IsTopLevelComment.Should().BeTrue();
        contract.IsReply.Should().BeFalse();
    }

    [Fact]
    public void ReplyContract_ShouldPreserveParentCommentAndThreadRoot()
    {
        var postId = Guid.NewGuid();
        var replyId = new FeedMessageId(Guid.NewGuid());
        var parentCommentId = new FeedMessageId(Guid.NewGuid());
        var threadRootId = new FeedMessageId(Guid.NewGuid());

        var contract = SocialThreadEntryContract.Reply(postId, replyId, parentCommentId, threadRootId);

        contract.PostId.Should().Be(postId);
        contract.EntryId.Should().Be(replyId);
        contract.Kind.Should().Be(SocialThreadEntryKind.Reply);
        contract.ParentCommentId.Should().Be(parentCommentId);
        contract.ThreadRootId.Should().Be(threadRootId);
        contract.IsTopLevelComment.Should().BeFalse();
        contract.IsReply.Should().BeTrue();
    }

    [Fact]
    public void PagingRules_TopLevelComments_ShouldUseTenThenTen()
    {
        var contract = SocialThreadPagingContractRules.For(SocialThreadPageKind.TopLevelComments);

        contract.PageKind.Should().Be(SocialThreadPageKind.TopLevelComments);
        contract.InitialPageSize.Should().Be(10);
        contract.LoadMorePageSize.Should().Be(10);
    }

    [Fact]
    public void PagingRules_ThreadReplies_ShouldUseFiveThenFive()
    {
        var contract = SocialThreadPagingContractRules.For(SocialThreadPageKind.ThreadReplies);

        contract.PageKind.Should().Be(SocialThreadPageKind.ThreadReplies);
        contract.InitialPageSize.Should().Be(5);
        contract.LoadMorePageSize.Should().Be(5);
    }
}
