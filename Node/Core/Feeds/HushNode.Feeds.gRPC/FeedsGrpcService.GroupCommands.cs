using Grpc.Core;
using HushNetwork.proto;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
namespace HushNode.Feeds.gRPC;

public partial class FeedsGrpcService
{
    public override async Task<GetInnerCircleResponse> GetInnerCircle(
        GetInnerCircleRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._innerCircleApplicationService.GetInnerCircleAsync(request.OwnerPublicAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "inner_circle.get.failed");
            return new GetInnerCircleResponse
            {
                Success = false,
                Exists = false,
                Message = "Internal server error"
            };
        }
    }

    // ===== Group Feed Creation & Membership Operations =====

    public override async Task<CreateInnerCircleResponse> CreateInnerCircle(
        CreateInnerCircleRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._innerCircleApplicationService.CreateInnerCircleAsync(
                request.OwnerPublicAddress,
                request.RequesterPublicAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "inner_circle.create.failed.unhandled");
            return new CreateInnerCircleResponse
            {
                Success = false,
                Message = "Internal server error",
                ErrorCode = "INNER_CIRCLE_INTERNAL_ERROR"
            };
        }
    }

    public override async Task<AddMembersToInnerCircleResponse> AddMembersToInnerCircle(
        AddMembersToInnerCircleRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._innerCircleApplicationService.AddMembersToInnerCircleAsync(
                request.OwnerPublicAddress,
                request.RequesterPublicAddress,
                request.Members);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "inner_circle.add_members.failed.unhandled");
            return new AddMembersToInnerCircleResponse
            {
                Success = false,
                Message = "Internal server error",
                ErrorCode = "INNER_CIRCLE_INTERNAL_ERROR"
            };
        }
    }

    public override async Task<NewGroupFeedResponse> CreateGroupFeed(
        NewGroupFeedRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation($"[CreateGroupFeed] FeedId: {request.FeedId}, Title: {request.Title}, IsPublic: {request.IsPublic}");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);

            // Check if feed already exists
            var existingFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
            if (existingFeed != null)
            {
                return new NewGroupFeedResponse
                {
                    Success = false,
                    Message = "A group with this ID already exists"
                };
            }

            var currentBlock = this._blockchainCache.LastBlockIndex;

            // Create the GroupFeed entity
            var groupFeed = new GroupFeed(
                feedId,
                request.Title,
                request.Description ?? string.Empty,
                request.IsPublic,
                currentBlock,
                CurrentKeyGeneration: 0);

            await this._feedsStorageService.CreateGroupFeed(groupFeed);

            // Add participants from request
            foreach (var participant in request.Participants)
            {
                var participantType = (ParticipantType)participant.ParticipantType;
                var participantEntity = new GroupFeedParticipantEntity(
                    feedId,
                    participant.ParticipantPublicAddress,
                    participantType,
                    currentBlock,
                    LeftAtBlock: null,
                    LastLeaveBlock: null);

                await this._feedsStorageService.AddParticipantAsync(feedId, participantEntity);
            }

            // Create initial KeyGeneration (KeyGen 0) with the provided encrypted keys
            var encryptedKeys = request.Participants
                .Select(p => new GroupFeedEncryptedKeyEntity(
                    feedId,
                    KeyGeneration: 0,
                    p.ParticipantPublicAddress,
                    p.EncryptedFeedKey))
                .ToList();

            var keyGeneration = new GroupFeedKeyGenerationEntity(
                feedId,
                KeyGeneration: 0,
                currentBlock,
                RotationTrigger.Join)  // Use Join for initial creation
            {
                EncryptedKeys = encryptedKeys
            };

            await this._feedsStorageService.CreateKeyRotationAsync(keyGeneration);

            // Create the Feed record (links to standard feeds system)
            var feed = new Feed(
                feedId,
                request.Title,
                FeedType.Group,
                currentBlock);

            // Add owner as feed participant
            var ownerParticipantProto = request.Participants.FirstOrDefault(p => p.ParticipantType == (int)ParticipantType.Owner);
            if (ownerParticipantProto != null)
            {
                feed.Participants.Add(new FeedParticipant(
                    feedId,
                    ownerParticipantProto.ParticipantPublicAddress,
                    ParticipantType.Owner,
                    ownerParticipantProto.EncryptedFeedKey)
                {
                    Feed = feed
                });
            }

            await this._feedsStorageService.CreateFeed(feed);

            _logger.LogInformation($"[CreateGroupFeed] Success - created group with {request.Participants.Count} participants");
            return new NewGroupFeedResponse
            {
                Success = true,
                Message = "Group created successfully"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CreateGroupFeed] ERROR");
            return new NewGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<JoinGroupFeedResponse> JoinGroupFeed(
        JoinGroupFeedRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupMembershipApplicationService.JoinGroupFeedAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[JoinGroupFeed] ERROR");
            return new JoinGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<LeaveGroupFeedResponse> LeaveGroupFeed(
        LeaveGroupFeedRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupMembershipApplicationService.LeaveGroupFeedAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[LeaveGroupFeed] ERROR");
            return new LeaveGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<GetSocialComposerContractResponse> GetSocialComposerContract(
        GetSocialComposerContractRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._socialComposerApplicationService.GetSocialComposerContractAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetSocialComposerContract] ERROR");
            return new GetSocialComposerContractResponse
            {
                Success = false,
                Message = "Failed to resolve social composer contract.",
                ErrorCode = "SOCIAL_COMPOSER_CONTRACT_ERROR",
                DefaultVisibility = SocialPostVisibilityProto.SocialPostVisibilityOpen,
                CanSubmit = false
            };
        }
    }

    public override async Task<CreateSocialPostResponse> CreateSocialPost(
        CreateSocialPostRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._socialPostApplicationService.CreateSocialPostAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[CreateSocialPost] ERROR");
            return new CreateSocialPostResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}",
                ErrorCode = "SOCIAL_POST_INTERNAL_ERROR"
            };
        }
    }

    public override async Task<GetSocialPostPermalinkResponse> GetSocialPostPermalink(
        GetSocialPostPermalinkRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._socialPostApplicationService.GetSocialPostPermalinkAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetSocialPostPermalink] ERROR");
            return new GetSocialPostPermalinkResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}",
                AccessState = SocialPermalinkAccessStateProto.SocialPermalinkAccessStateNotFound,
                ErrorCode = "SOCIAL_POST_INTERNAL_ERROR",
                OpenGraph = new SocialPostOpenGraphProto
                {
                    Title = "Private post",
                    Description = "Sign in to view this post.",
                    IsGenericPrivate = true,
                    CacheControl = "no-store"
                }
            };
        }
    }

    public override async Task<GetSocialFeedWallResponse> GetSocialFeedWall(
        GetSocialFeedWallRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._socialPostApplicationService.GetSocialFeedWallAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetSocialFeedWall] ERROR");
            return new GetSocialFeedWallResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}",
                ErrorCode = "SOCIAL_FEEDWALL_INTERNAL_ERROR"
            };
        }
    }

    public override async Task<GetSocialCommentsPageResponse> GetSocialCommentsPage(
        GetSocialCommentsPageRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._socialThreadApplicationService.GetSocialCommentsPageAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetSocialCommentsPage] ERROR");
            return new GetSocialCommentsPageResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}",
                Paging = new SocialThreadPageContractProto
                {
                    InitialPageSize = 10,
                    LoadMorePageSize = 10
                },
                HasMore = false
            };
        }
    }

    public override async Task<GetSocialThreadRepliesResponse> GetSocialThreadReplies(
        GetSocialThreadRepliesRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._socialThreadApplicationService.GetSocialThreadRepliesAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetSocialThreadReplies] ERROR");
            return new GetSocialThreadRepliesResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}",
                Paging = new SocialThreadPageContractProto
                {
                    InitialPageSize = 5,
                    LoadMorePageSize = 5
                },
                HasMore = false
            };
        }
    }

    // ===== Group Feed Admin Operations (FEAT-017) =====

    public override async Task<AddMemberToGroupFeedResponse> AddMemberToGroupFeed(
        AddMemberToGroupFeedRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupMembershipApplicationService.AddMemberToGroupFeedAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AddMemberToGroupFeed] ERROR");
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<BlockMemberResponse> BlockMember(
        BlockMemberRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.BlockMemberAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BlockMember] ERROR");
            return new BlockMemberResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UnblockMemberResponse> UnblockMember(
        UnblockMemberRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.UnblockMemberAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UnblockMember] ERROR");
            return new UnblockMemberResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<BanFromGroupFeedResponse> BanFromGroupFeed(
        BanFromGroupFeedRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.BanFromGroupFeedAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[BanFromGroupFeed] ERROR");
            return new BanFromGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UnbanFromGroupFeedResponse> UnbanFromGroupFeed(
        UnbanFromGroupFeedRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.UnbanFromGroupFeedAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UnbanFromGroupFeed] ERROR");
            return new UnbanFromGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<PromoteToAdminResponse> PromoteToAdmin(
        PromoteToAdminRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.PromoteToAdminAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[PromoteToAdmin] ERROR");
            return new PromoteToAdminResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UpdateGroupFeedTitleResponse> UpdateGroupFeedTitle(
        UpdateGroupFeedTitleRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.UpdateGroupFeedTitleAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpdateGroupFeedTitle] ERROR");
            return new UpdateGroupFeedTitleResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UpdateGroupFeedDescriptionResponse> UpdateGroupFeedDescription(
        UpdateGroupFeedDescriptionRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.UpdateGroupFeedDescriptionAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpdateGroupFeedDescription] ERROR");
            return new UpdateGroupFeedDescriptionResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<UpdateGroupFeedSettingsResponse> UpdateGroupFeedSettings(
        UpdateGroupFeedSettingsRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.UpdateGroupFeedSettingsAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[UpdateGroupFeedSettings] ERROR");
            return new UpdateGroupFeedSettingsResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<DeleteGroupFeedResponse> DeleteGroupFeed(
        DeleteGroupFeedRequest request,
        ServerCallContext context)
    {
        try
        {
            return await this._groupAdministrationApplicationService.DeleteGroupFeedAsync(request);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[DeleteGroupFeed] ERROR");
            return new DeleteGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }
}


