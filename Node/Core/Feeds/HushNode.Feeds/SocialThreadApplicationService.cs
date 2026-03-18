using Google.Protobuf;
using HushNetwork.proto;
using HushNode.Feeds.gRPC;
using HushNode.Feeds.Storage;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public sealed class SocialThreadApplicationService(
    ISocialThreadService socialThreadService,
    IFeedsStorageService feedsStorageService) : ISocialThreadApplicationService
{
    private readonly ISocialThreadService _socialThreadService = socialThreadService;
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task<GetSocialCommentsPageResponse> GetSocialCommentsPageAsync(GetSocialCommentsPageRequest request)
    {
        if (!Guid.TryParse(request.PostId, out var postId))
        {
            return new GetSocialCommentsPageResponse
            {
                Success = false,
                Message = "Invalid PostId format.",
                Paging = MapPaging(SocialThreadPagingContractRules.For(SocialThreadPageKind.TopLevelComments)),
                HasMore = false
            };
        }

        FeedMessageId? beforeEntryId = null;
        if (!string.IsNullOrWhiteSpace(request.BeforeEntryId))
        {
            if (!Guid.TryParse(request.BeforeEntryId, out var beforeGuid))
            {
                return new GetSocialCommentsPageResponse
                {
                    Success = false,
                    Message = "Invalid BeforeEntryId format.",
                    Paging = MapPaging(SocialThreadPagingContractRules.For(SocialThreadPageKind.TopLevelComments)),
                    HasMore = false
                };
            }

            beforeEntryId = new FeedMessageId(beforeGuid);
        }

        var result = await _socialThreadService.GetCommentsPageAsync(
            postId,
            request.RequesterPublicAddress,
            request.IsAuthenticated,
            request.Limit,
            beforeEntryId);

        var response = new GetSocialCommentsPageResponse
        {
            Success = result.Success,
            Message = result.Message,
            Paging = MapPaging(result.Paging),
            HasMore = result.HasMore
        };
        foreach (var entry in result.Entries)
        {
            response.Comments.Add(await MapEntryAsync(
                entry,
                request.RequesterPublicAddress,
                request.IsAuthenticated));
        }
        return response;
    }

    public async Task<GetSocialThreadRepliesResponse> GetSocialThreadRepliesAsync(GetSocialThreadRepliesRequest request)
    {
        if (!Guid.TryParse(request.PostId, out var postId))
        {
            return new GetSocialThreadRepliesResponse
            {
                Success = false,
                Message = "Invalid PostId format.",
                Paging = MapPaging(SocialThreadPagingContractRules.For(SocialThreadPageKind.ThreadReplies)),
                HasMore = false
            };
        }

        if (!Guid.TryParse(request.ThreadRootId, out var threadRootGuid))
        {
            return new GetSocialThreadRepliesResponse
            {
                Success = false,
                Message = "Invalid ThreadRootId format.",
                Paging = MapPaging(SocialThreadPagingContractRules.For(SocialThreadPageKind.ThreadReplies)),
                HasMore = false
            };
        }

        FeedMessageId? beforeEntryId = null;
        if (!string.IsNullOrWhiteSpace(request.BeforeEntryId))
        {
            if (!Guid.TryParse(request.BeforeEntryId, out var beforeGuid))
            {
                return new GetSocialThreadRepliesResponse
                {
                    Success = false,
                    Message = "Invalid BeforeEntryId format.",
                    Paging = MapPaging(SocialThreadPagingContractRules.For(SocialThreadPageKind.ThreadReplies)),
                    HasMore = false
                };
            }

            beforeEntryId = new FeedMessageId(beforeGuid);
        }

        var result = await _socialThreadService.GetRepliesPageAsync(
            postId,
            new FeedMessageId(threadRootGuid),
            request.RequesterPublicAddress,
            request.IsAuthenticated,
            request.Limit,
            beforeEntryId);

        var response = new GetSocialThreadRepliesResponse
        {
            Success = result.Success,
            Message = result.Message,
            Paging = MapPaging(result.Paging),
            HasMore = result.HasMore
        };
        foreach (var entry in result.Entries)
        {
            response.Replies.Add(await MapEntryAsync(
                entry,
                request.RequesterPublicAddress,
                request.IsAuthenticated));
        }
        return response;
    }

    private async Task<SocialThreadEntryProto> MapEntryAsync(
        RankedSocialThreadEntry entry,
        string? requesterPublicAddress,
        bool isAuthenticated)
    {
        var proto = new SocialThreadEntryProto
        {
            PostId = entry.ThreadEntry.PostId.ToString("D"),
            EntryId = entry.ThreadEntry.EntryId.Value.ToString("D"),
            Kind = entry.ThreadEntry.Kind == SocialThreadEntryKind.Comment
                ? SocialThreadEntryKindProto.SocialThreadEntryKindComment
                : SocialThreadEntryKindProto.SocialThreadEntryKindReply,
            ThreadRootId = entry.ThreadEntry.ThreadRootId.Value.ToString("D"),
            ReactionScopeId = entry.ThreadEntry.PostId.ToString("D"),
            CreatedAtUnixMs = new DateTimeOffset(entry.Message.Timestamp.Value).ToUnixTimeMilliseconds(),
            ReactionCount = entry.ReactionCount,
            AuthorPublicAddress = entry.Message.IssuerPublicAddress,
            Content = entry.Message.MessageContent ?? string.Empty,
            ChildReplyCount = entry.ChildReplyCount
        };

        if (entry.ThreadEntry.ParentCommentId is { } parentCommentId)
        {
            proto.ParentCommentId = parentCommentId.Value.ToString("D");
        }

        if (entry.Message.AuthorCommitment is { Length: > 0 })
        {
            proto.AuthorCommitment = ByteString.CopyFrom(entry.Message.AuthorCommitment);
        }

        if (isAuthenticated && !string.IsNullOrWhiteSpace(requesterPublicAddress))
        {
            var followState = await _feedsStorageService.GetSocialFollowStateAsync(
                requesterPublicAddress,
                entry.Message.IssuerPublicAddress);
            proto.FollowState = new SocialAuthorFollowStateProto
            {
                IsFollowing = followState.IsFollowing,
                CanFollow = followState.CanFollow
            };
        }
        else
        {
            proto.FollowState = new SocialAuthorFollowStateProto
            {
                IsFollowing = false,
                CanFollow = false
            };
        }

        return proto;
    }

    private static SocialThreadPageContractProto MapPaging(SocialThreadPageContract paging) =>
        new()
        {
            InitialPageSize = paging.InitialPageSize,
            LoadMorePageSize = paging.LoadMorePageSize
        };
}
