using System.Collections.Concurrent;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;

namespace HushNode.Feeds.gRPC;

public sealed class SocialPostApplicationService(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    ISocialPostNotificationService socialPostNotificationService) : ISocialPostApplicationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ISocialPostNotificationService _socialPostNotificationService = socialPostNotificationService;

    private readonly ConcurrentDictionary<Guid, StoredSocialPost> _posts = new();

    public async Task<CreateSocialPostResponse> CreateSocialPostAsync(CreateSocialPostRequest request)
    {
        if (!Guid.TryParse(request.PostId, out var postId))
        {
            return FailCreate("Invalid PostId format.", "SOCIAL_POST_INVALID_ID");
        }

        if (string.IsNullOrWhiteSpace(request.AuthorPublicAddress))
        {
            return FailCreate("AuthorPublicAddress is required.", "SOCIAL_POST_INVALID_AUTHOR");
        }

        var audience = MapAudience(request.Audience);
        var audienceValidation = SocialPostContractRules.ValidateAudience(audience);
        if (!audienceValidation.IsValid)
        {
            return FailCreate(audienceValidation.Message, audienceValidation.ErrorCode.ToString());
        }

        var attachments = request.Attachments
            .Select(MapAttachment)
            .ToArray();
        var attachmentValidation = SocialPostContractRules.ValidateAttachments(attachments);
        if (!attachmentValidation.IsValid)
        {
            return FailCreate(attachmentValidation.Message, attachmentValidation.ErrorCode.ToString());
        }

        var normalizedCircles = audience.CircleFeedIds
            .Select(x => x.Trim())
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var authorizedPrivateViewers = new HashSet<string>(StringComparer.Ordinal);
        var authorAddress = request.AuthorPublicAddress.Trim();

        if (audience.Visibility == SocialPostVisibility.Private)
        {
            foreach (var circleFeedIdRaw in normalizedCircles)
            {
                if (!Guid.TryParse(circleFeedIdRaw, out var circleGuid))
                {
                    return FailCreate("Selected circle contains an invalid id.", "SOCIAL_POST_CIRCLE_INVALID");
                }

                var circleFeed = await _feedsStorageService.GetGroupFeedAsync(new FeedId(circleGuid));
                if (circleFeed == null || circleFeed.IsDeleted || circleFeed.OwnerPublicAddress != authorAddress)
                {
                    return FailCreate("One or more selected circles are missing or invalid.", "SOCIAL_POST_CIRCLE_INVALID");
                }

                var activeParticipants = await _feedsStorageService.GetActiveParticipantsAsync(circleFeed.FeedId);
                foreach (var participant in activeParticipants)
                {
                    authorizedPrivateViewers.Add(participant.ParticipantPublicAddress);
                }
            }
        }

        authorizedPrivateViewers.Add(authorAddress);

        var createdAtBlock = _blockchainCache.LastBlockIndex.Value;
        var storedPost = new StoredSocialPost(
            PostId: postId,
            AuthorPublicAddress: authorAddress,
            Content: request.Content ?? string.Empty,
            AudienceVisibility: audience.Visibility,
            CircleFeedIds: normalizedCircles,
            CreatedAtBlock: createdAtBlock,
            AuthorizedPrivateViewers: authorizedPrivateViewers);

        if (!_posts.TryAdd(postId, storedPost))
        {
            return FailCreate("Post with this id already exists.", "SOCIAL_POST_DUPLICATE_ID");
        }

        await _socialPostNotificationService.NotifyPostCreatedAsync(
            authorAddress,
            request.Content ?? string.Empty,
            audience.Visibility == SocialPostVisibility.Private,
            postId.ToString("D"),
            authorizedPrivateViewers.ToArray());

        return new CreateSocialPostResponse
        {
            Success = true,
            Message = "Social post accepted.",
            Permalink = $"/social/post/{postId:D}"
        };
    }

    public Task<GetSocialPostPermalinkResponse> GetSocialPostPermalinkAsync(GetSocialPostPermalinkRequest request)
    {
        if (!Guid.TryParse(request.PostId, out var postId))
        {
            return Task.FromResult(new GetSocialPostPermalinkResponse
            {
                Success = false,
                Message = "Invalid PostId format.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateNotFound,
                OpenGraph = GenericPrivateOg()
            });
        }

        if (!_posts.TryGetValue(postId, out var post))
        {
            return Task.FromResult(new GetSocialPostPermalinkResponse
            {
                Success = false,
                Message = "Post not found.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateNotFound,
                OpenGraph = GenericPrivateOg()
            });
        }

        if (post.AudienceVisibility == SocialPostVisibility.Open)
        {
            return Task.FromResult(new GetSocialPostPermalinkResponse
            {
                Success = true,
                Message = "Post resolved.",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed,
                PostId = post.PostId.ToString(),
                AuthorPublicAddress = post.AuthorPublicAddress,
                Content = post.Content,
                CreatedAtBlock = post.CreatedAtBlock,
                CanInteract = request.IsAuthenticated,
                DenialKind = SocialPermalinkDenialKindProto.SocialPermalinkDenialKindNone,
                OpenGraph = PublicOg(post.Content)
            });
        }

        if (!request.IsAuthenticated)
        {
            return Task.FromResult(new GetSocialPostPermalinkResponse
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
            });
        }

        var requester = request.RequesterPublicAddress?.Trim();
        var isAllowed = !string.IsNullOrWhiteSpace(requester) &&
                        post.AuthorizedPrivateViewers.Contains(requester);

        if (!isAllowed)
        {
            return Task.FromResult(new GetSocialPostPermalinkResponse
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
            });
        }

        var response = new GetSocialPostPermalinkResponse
        {
            Success = true,
            Message = "Post resolved.",
            AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateAllowed,
            PostId = post.PostId.ToString(),
            AuthorPublicAddress = post.AuthorPublicAddress,
            Content = post.Content,
            CreatedAtBlock = post.CreatedAtBlock,
            CanInteract = true,
            DenialKind = SocialPermalinkDenialKindProto.SocialPermalinkDenialKindNone,
            OpenGraph = PublicOg(post.Content)
        };

        response.CircleFeedIds.AddRange(post.CircleFeedIds);
        return Task.FromResult(response);
    }

    private static CreateSocialPostResponse FailCreate(string message, string code) =>
        new()
        {
            Success = false,
            Message = message,
            ErrorCode = code
        };

    private static SocialPostAudience MapAudience(SocialPostAudienceProto? audience)
    {
        if (audience == null)
        {
            return new SocialPostAudience(SocialPostVisibility.Open, Array.Empty<string>());
        }

        var visibility = audience.Visibility switch
        {
            SocialPostVisibilityProto.SocialPostVisibilityPrivate => SocialPostVisibility.Private,
            _ => SocialPostVisibility.Open
        };

        return new SocialPostAudience(visibility, audience.CircleFeedIds.ToArray());
    }

    private static SocialPostAttachment MapAttachment(SocialPostAttachmentProto attachment)
    {
        var kind = attachment.Kind switch
        {
            SocialPostAttachmentKindProto.SocialPostAttachmentKindVideo => SocialPostAttachmentKind.Video,
            _ => SocialPostAttachmentKind.Image
        };

        return new SocialPostAttachment(
            attachment.AttachmentId,
            attachment.MimeType,
            attachment.Size,
            attachment.FileName,
            attachment.Hash,
            kind);
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

    private sealed record StoredSocialPost(
        Guid PostId,
        string AuthorPublicAddress,
        string Content,
        SocialPostVisibility AudienceVisibility,
        string[] CircleFeedIds,
        long CreatedAtBlock,
        HashSet<string> AuthorizedPrivateViewers);
}
