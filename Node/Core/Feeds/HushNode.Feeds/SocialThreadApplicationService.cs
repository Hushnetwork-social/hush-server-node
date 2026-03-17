using Google.Protobuf;
using HushNetwork.proto;
using HushNode.Feeds.gRPC;
using HushShared.Feeds.Model;

namespace HushNode.Feeds;

public sealed class SocialThreadApplicationService(
    ISocialThreadService socialThreadService) : ISocialThreadApplicationService
{
    private readonly ISocialThreadService _socialThreadService = socialThreadService;

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
        response.Comments.AddRange(result.Entries.Select(MapEntry));
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
        response.Replies.AddRange(result.Entries.Select(MapEntry));
        return response;
    }

    private static SocialThreadEntryProto MapEntry(RankedSocialThreadEntry entry)
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

        return proto;
    }

    private static SocialThreadPageContractProto MapPaging(SocialThreadPageContract paging) =>
        new()
        {
            InitialPageSize = paging.InitialPageSize,
            LoadMorePageSize = paging.LoadMorePageSize
        };
}
