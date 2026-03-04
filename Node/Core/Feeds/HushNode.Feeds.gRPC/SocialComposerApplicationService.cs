using HushNetwork.proto;
using HushNode.Feeds.Storage;

namespace HushNode.Feeds.gRPC;

public sealed class SocialComposerApplicationService(
    IFeedsStorageService feedsStorageService) : ISocialComposerApplicationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;

    public async Task<GetSocialComposerContractResponse> GetSocialComposerContractAsync(GetSocialComposerContractRequest request)
    {
        var ownerAddress = request.OwnerPublicAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerAddress))
        {
            return new GetSocialComposerContractResponse
            {
                Success = false,
                Message = "OwnerPublicAddress is required.",
                ErrorCode = "SOCIAL_COMPOSER_INVALID_OWNER",
                DefaultVisibility = SocialPostVisibilityProto.SocialPostVisibilityOpen,
                CanSubmit = false
            };
        }

        var circles = await _feedsStorageService.GetCirclesForOwnerAsync(ownerAddress);
        var defaultVisibility = request.PreferredVisibility == SocialPostVisibilityProto.SocialPostVisibilityPrivate
            ? SocialPostVisibilityProto.SocialPostVisibilityPrivate
            : SocialPostVisibilityProto.SocialPostVisibilityOpen;

        var selectedCircleFeedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (defaultVisibility == SocialPostVisibilityProto.SocialPostVisibilityPrivate)
        {
            var innerCircle = circles.FirstOrDefault(x => x.IsInnerCircle);
            if (innerCircle != null)
            {
                selectedCircleFeedIds.Add(innerCircle.FeedId.ToString());
            }
        }

        var response = new GetSocialComposerContractResponse
        {
            Success = true,
            Message = "Composer contract resolved.",
            DefaultVisibility = defaultVisibility,
            CanSubmit = defaultVisibility == SocialPostVisibilityProto.SocialPostVisibilityOpen || selectedCircleFeedIds.Count > 0
        };

        foreach (var circle in circles)
        {
            var feedId = circle.FeedId.ToString();
            var isSelected = selectedCircleFeedIds.Contains(feedId);

            response.AvailableCircles.Add(new ComposerCircleContractProto
            {
                FeedId = feedId,
                CircleName = circle.Name,
                IsInnerCircle = circle.IsInnerCircle,
                MemberCount = circle.MemberCount,
                IsSelectedByDefault = isSelected,
                IsRemovable = !isSelected || selectedCircleFeedIds.Count > 1
            });
        }

        response.SelectedCircleFeedIds.AddRange(selectedCircleFeedIds);

        if (defaultVisibility == SocialPostVisibilityProto.SocialPostVisibilityPrivate && selectedCircleFeedIds.Count == 0)
        {
            response.CanSubmit = false;
            response.ErrorCode = "SOCIAL_POST_PRIVATE_REQUIRES_CIRCLE";
            response.Message = "At least one private circle is required to submit.";
        }

        return response;
    }
}
