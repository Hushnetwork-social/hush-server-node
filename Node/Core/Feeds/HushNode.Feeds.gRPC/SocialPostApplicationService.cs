using HushNetwork.proto;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Olimpo;
using Google.Protobuf;

namespace HushNode.Feeds.gRPC;

public sealed class SocialPostApplicationService(
    IFeedsStorageService feedsStorageService,
    IAttachmentStorageService attachmentStorageService,
    IFeedMessageStorageService feedMessageStorageService) : ISocialPostApplicationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IAttachmentStorageService _attachmentStorageService = attachmentStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;

    public Task<CreateSocialPostResponse> CreateSocialPostAsync(CreateSocialPostRequest _)
    {
        // FEAT-086 contract: social post creation must be submitted as a signed blockchain transaction.
        return Task.FromResult(new CreateSocialPostResponse
        {
            Success = false,
            Message = "Use signed blockchain transaction CreateSocialPostPayload.",
            ErrorCode = "SOCIAL_POST_BLOCKCHAIN_REQUIRED"
        });
    }

    public async Task<GetSocialPostPermalinkResponse> GetSocialPostPermalinkAsync(GetSocialPostPermalinkRequest request)
    {
        if (!Guid.TryParse(request.PostId, out var postId))
        {
            return new GetSocialPostPermalinkResponse
            {
                Success = false,
                Message = "Invalid PostId format.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateNotFound,
                OpenGraph = GenericPrivateOg()
            };
        }

        var post = await this._feedsStorageService.GetSocialPostAsync(postId);
        if (post == null)
        {
            return new GetSocialPostPermalinkResponse
            {
                Success = false,
                Message = "Post not found.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateNotFound,
                OpenGraph = GenericPrivateOg()
            };
        }

        if (post.AudienceVisibility == SocialPostVisibility.Open)
        {
            var attachments = await this.GetSocialPostAttachmentsAsync(post.PostId);
            var openResponse = new GetSocialPostPermalinkResponse
            {
                Success = true,
                Message = "Post resolved.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed,
                PostId = post.PostId.ToString("D"),
                ReactionScopeId = post.ReactionScopeId.ToString("D"),
                AuthorPublicAddress = post.AuthorPublicAddress,
                Content = post.Content,
                CreatedAtBlock = post.CreatedAtBlock.Value,
                CreatedAtUnixMs = post.CreatedAtUnixMs,
                CanInteract = request.IsAuthenticated,
                DenialKind = SocialPermalinkDenialKindProto.SocialPermalinkDenialKindNone,
                OpenGraph = PublicOg(post.Content),
                Attachments = { attachments }
            };
            if (post.AuthorCommitment != null)
            {
                openResponse.AuthorCommitment = ByteString.CopyFrom(post.AuthorCommitment);
            }
            return openResponse;
        }

        if (!request.IsAuthenticated)
        {
            return new GetSocialPostPermalinkResponse
            {
                Success = true,
                Message = "Authentication is required.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateGuestDenied,
                CanInteract = false,
                ErrorCode = "SOCIAL_POST_AUTH_REQUIRED",
                DenialKind = SocialPermalinkDenialKindProto.SocialPermalinkDenialKindGuestCreateAccount,
                DenialTitle = "Create your HushNetwork account",
                DenialBody = "This private post is only visible to authorized members.",
                PrimaryCtaLabel = "Create account",
                PrimaryCtaRoute = "/register",
                OpenGraph = GenericPrivateOg()
            };
        }

        var requester = request.RequesterPublicAddress?.Trim();
        var isRequesterAuthor = !string.IsNullOrWhiteSpace(requester) &&
                                string.Equals(requester, post.AuthorPublicAddress, StringComparison.Ordinal);

        var circleFeedIds = post.AudienceCircles.Select(x => x.CircleFeedId).ToArray();
        var hasCircleAccess = !string.IsNullOrWhiteSpace(requester) &&
            await this._feedsStorageService.IsUserInAnyActiveCircleAsync(requester, circleFeedIds);

        if (!isRequesterAuthor && !hasCircleAccess)
        {
            return new GetSocialPostPermalinkResponse
            {
                Success = true,
                Message = "You do not have permission to view this post.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateUnauthorizedDenied,
                CanInteract = false,
                ErrorCode = "SOCIAL_POST_ACCESS_DENIED",
                DenialKind = SocialPermalinkDenialKindProto.SocialPermalinkDenialKindUnauthorizedRequestAccess,
                DenialTitle = "You do not have permission to view this post",
                DenialBody = "Ask the post owner to grant access to this circle.",
                PrimaryCtaLabel = "Request access",
                PrimaryCtaRoute = "/social/following",
                OpenGraph = GenericPrivateOg()
            };
        }

        var response = new GetSocialPostPermalinkResponse
        {
            Success = true,
            Message = "Post resolved.",
            AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed,
            PostId = post.PostId.ToString("D"),
            ReactionScopeId = post.ReactionScopeId.ToString("D"),
            AuthorPublicAddress = post.AuthorPublicAddress,
            Content = post.Content,
            CreatedAtBlock = post.CreatedAtBlock.Value,
            CreatedAtUnixMs = post.CreatedAtUnixMs,
            CanInteract = true,
            DenialKind = SocialPermalinkDenialKindProto.SocialPermalinkDenialKindNone,
            OpenGraph = PublicOg(post.Content)
        };

        response.CircleFeedIds.AddRange(post.AudienceCircles.Select(x => x.CircleFeedId.ToString()));
        response.Attachments.AddRange(await this.GetSocialPostAttachmentsAsync(post.PostId));
        if (post.AuthorCommitment != null)
        {
            response.AuthorCommitment = ByteString.CopyFrom(post.AuthorCommitment);
        }
        return response;
    }

    public async Task<GetSocialFeedWallResponse> GetSocialFeedWallAsync(GetSocialFeedWallRequest request)
    {
        var limit = request.Limit <= 0 ? 50 : Math.Min(request.Limit, 200);
        var posts = await this._feedsStorageService.GetLatestSocialPostsAsync(limit);
        var requester = request.RequesterPublicAddress?.Trim();
        var isAuthenticated = request.IsAuthenticated && !string.IsNullOrWhiteSpace(requester);

        var visiblePosts = new List<SocialFeedWallPostProto>();
        foreach (var post in posts)
        {
            var canView = post.AudienceVisibility == SocialPostVisibility.Open;

            if (!canView && isAuthenticated && requester != null)
            {
                var isAuthor = string.Equals(post.AuthorPublicAddress, requester, StringComparison.Ordinal);
                if (isAuthor)
                {
                    canView = true;
                }
                else
                {
                    var circleFeedIds = post.AudienceCircles.Select(x => x.CircleFeedId).ToArray();
                    canView = await this._feedsStorageService.IsUserInAnyActiveCircleAsync(requester, circleFeedIds);
                }
            }

            if (!canView)
            {
                continue;
            }

            var postProto = new SocialFeedWallPostProto
            {
                PostId = post.PostId.ToString("D"),
                ReactionScopeId = post.ReactionScopeId.ToString("D"),
                AuthorPublicAddress = post.AuthorPublicAddress,
                Content = post.Content,
                CreatedAtBlock = post.CreatedAtBlock.Value,
                CreatedAtUnixMs = post.CreatedAtUnixMs,
                ReplyCount = await this.GetThreadEntryCountAsync(post.PostId),
                Visibility = post.AudienceVisibility == SocialPostVisibility.Private
                    ? SocialPostVisibilityProto.SocialPostVisibilityPrivate
                    : SocialPostVisibilityProto.SocialPostVisibilityOpen
            };
            if (post.AuthorCommitment != null)
            {
                postProto.AuthorCommitment = ByteString.CopyFrom(post.AuthorCommitment);
            }
            postProto.CircleFeedIds.AddRange(post.AudienceCircles.Select(x => x.CircleFeedId.ToString()));
            postProto.Attachments.AddRange(await this.GetSocialPostAttachmentsAsync(post.PostId));
            visiblePosts.Add(postProto);
        }

        return new GetSocialFeedWallResponse
        {
            Success = true,
            Message = "Feed wall resolved.",
            Posts = { visiblePosts }
        };
    }

    private static SocialPostOpenGraphProto GenericPrivateOg() =>
        new()
        {
            Title = "Private post",
            Description = "Sign in to view this post.",
            IsGenericPrivate = true,
            CacheControl = "no-store"
        };

    private static SocialPostOpenGraphProto PublicOg(string content)
    {
        var excerpt = content.Length <= 140 ? content : content[..140];
        return new SocialPostOpenGraphProto
        {
            Title = "HushSocial post",
            Description = excerpt,
            IsGenericPrivate = false,
            CacheControl = "public, max-age=300"
        };
    }

    private async Task<IReadOnlyList<SocialPostAttachmentProto>> GetSocialPostAttachmentsAsync(Guid postId)
    {
        var attachments = await this._attachmentStorageService.GetByMessageIdAsync(new FeedMessageId(postId));
        return attachments
            .OrderBy(x => x.CreatedAt)
            .Select(attachment => new SocialPostAttachmentProto
            {
                AttachmentId = attachment.Id,
                MimeType = attachment.MimeType,
                Size = attachment.OriginalSize,
                FileName = attachment.FileName,
                Hash = attachment.Hash,
                Kind = attachment.MimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase)
                    ? SocialPostAttachmentKindProto.SocialPostAttachmentKindVideo
                    : SocialPostAttachmentKindProto.SocialPostAttachmentKindImage
            })
            .ToArray();
    }

    private async Task<int> GetThreadEntryCountAsync(Guid postId)
    {
        var messages = await this._feedMessageStorageService.RetrieveLastFeedMessagesForFeedAsync(
            new FeedId(postId),
            new BlockIndex(0));
        return messages.Count();
    }
}
