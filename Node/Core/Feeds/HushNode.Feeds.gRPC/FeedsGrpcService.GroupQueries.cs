using Grpc.Core;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
namespace HushNode.Feeds.gRPC;

public partial class FeedsGrpcService
{
    // ===== Group Feed Query Operations (FEAT-017) =====

    public override async Task<GetGroupMembersResponse> GetGroupMembers(
        GetGroupMembersRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation($"[GetGroupMembers] FeedId: {request.FeedId}");

        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var response = new GetGroupMembersResponse();

        // Cache-aside pattern: Try cache first
        var cachedMembers = await this._groupMembersCacheService.GetGroupMembersAsync(feedId);

        if (cachedMembers != null)
        {
            _logger.LogInformation($"[GetGroupMembers] Cache hit - {cachedMembers.Members.Count} members");

            // Convert cached members to proto response
            foreach (var member in cachedMembers.Members)
            {
                var memberProto = new GroupFeedMemberProto
                {
                    PublicAddress = member.PublicAddress,
                    ParticipantType = member.ParticipantType,
                    JoinedAtBlock = member.JoinedAtBlock,
                    DisplayName = member.DisplayName
                };

                if (member.LeftAtBlock != null)
                {
                    memberProto.LeftAtBlock = member.LeftAtBlock.Value;
                }

                response.Members.Add(memberProto);
            }

            return response;
        }

        _logger.LogInformation($"[GetGroupMembers] Cache miss - fetching from DB");

        // Cache miss: Get from database and resolve display names
        // Use GetActiveParticipantsAsync to only return current members (excludes left/banned)
        var allParticipants = await this._feedsStorageService.GetActiveParticipantsAsync(feedId);

        var membersToCache = new List<CachedGroupMember>();

        foreach (var participant in allParticipants)
        {
            // Get display name for each participant (uses identity cache internally)
            var displayName = await this.ExtractDisplayName(participant.ParticipantPublicAddress);

            var memberProto = new GroupFeedMemberProto
            {
                PublicAddress = participant.ParticipantPublicAddress,
                ParticipantType = (int)participant.ParticipantType,
                JoinedAtBlock = participant.JoinedAtBlock.Value,
                DisplayName = displayName
            };

            // Include LeftAtBlock if the member has left
            if (participant.LeftAtBlock != null)
            {
                memberProto.LeftAtBlock = participant.LeftAtBlock.Value;
            }

            response.Members.Add(memberProto);

            // Build cache entry
            membersToCache.Add(new CachedGroupMember(
                participant.ParticipantPublicAddress,
                displayName,
                (int)participant.ParticipantType,
                participant.JoinedAtBlock.Value,
                participant.LeftAtBlock?.Value));
        }

        // Populate cache for future requests
        await this._groupMembersCacheService.SetGroupMembersAsync(feedId, new CachedGroupMembers(membersToCache));

        _logger.LogInformation($"[GetGroupMembers] Returning {response.Members.Count} members (cached for future requests)");
        return response;
    }

    public override async Task<GetKeyGenerationsResponse> GetKeyGenerations(
        GetKeyGenerationsRequest request,
        ServerCallContext context)
    {
        var userAddress = request.UserPublicAddress ?? string.Empty;
        _logger.LogInformation($"[GetKeyGenerations] FeedId: {request.FeedId}, UserAddress: {userAddress.Substring(0, Math.Min(10, userAddress.Length))}...");

        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var response = new GetKeyGenerationsResponse();

        // FEAT-050: Try cache first for key generations
        var cachedKeyGenerations = await this._feedParticipantsCacheService.GetKeyGenerationsAsync(feedId);
        if (cachedKeyGenerations != null)
        {
            _logger.LogInformation($"[GetKeyGenerations] Cache hit, processing {cachedKeyGenerations.KeyGenerations.Count} key generations");

            // Filter cached key generations for this user
            foreach (var cachedKg in cachedKeyGenerations.KeyGenerations)
            {
                // Check if this user has an encrypted key for this generation
                if (!cachedKg.EncryptedKeysByMember.TryGetValue(userAddress, out var userEncryptedKey))
                {
                    // User doesn't have a key for this generation (wasn't a member)
                    continue;
                }

                var keyGenProto = new KeyGenerationProto
                {
                    KeyGeneration = cachedKg.Version,
                    EncryptedKey = userEncryptedKey,
                    ValidFromBlock = cachedKg.ValidFromBlock
                };

                // ValidToBlock is optional - only set if present
                if (cachedKg.ValidToBlock.HasValue)
                {
                    keyGenProto.ValidToBlock = cachedKg.ValidToBlock.Value;
                }

                response.KeyGenerations.Add(keyGenProto);
            }

            _logger.LogInformation($"[GetKeyGenerations] Returning {response.KeyGenerations.Count} key generations for user (from cache)");
            return response;
        }

        _logger.LogInformation($"[GetKeyGenerations] Cache miss, querying database");

        // Cache miss - query ALL key generations from database and populate cache
        var allKeyGenerations = await this._feedsStorageService.GetAllKeyGenerationsAsync(feedId);

        if (allKeyGenerations.Count > 0)
        {
            // Convert to cache DTOs and populate cache (fire-and-forget)
            // ValidToBlock is the ValidFromBlock of the next generation, or null for the latest
            var keyGenList = allKeyGenerations.ToList();
            var cacheDtos = new List<KeyGenerationCacheDto>();
            for (int i = 0; i < keyGenList.Count; i++)
            {
                var kg = keyGenList[i];
                var validToBlock = (i < keyGenList.Count - 1) ? keyGenList[i + 1].ValidFromBlock.Value : (long?)null;

                cacheDtos.Add(new KeyGenerationCacheDto
                {
                    Version = kg.KeyGeneration,
                    ValidFromBlock = kg.ValidFromBlock.Value,
                    ValidToBlock = validToBlock,
                    EncryptedKeysByMember = kg.EncryptedKeys.ToDictionary(
                        ek => ek.MemberPublicAddress,
                        ek => ek.EncryptedAesKey)
                });
            }

            var cacheWrapper = new CachedKeyGenerations { KeyGenerations = cacheDtos };
            _ = this._feedParticipantsCacheService.SetKeyGenerationsAsync(feedId, cacheWrapper);
        }

        // Filter and return key generations for this user
        foreach (var kg in allKeyGenerations)
        {
            // Find this user's encrypted key for this KeyGeneration
            var userEncryptedKey = kg.EncryptedKeys
                .FirstOrDefault(ek => ek.MemberPublicAddress == userAddress)
                ?.EncryptedAesKey ?? string.Empty;

            if (string.IsNullOrEmpty(userEncryptedKey))
            {
                // User doesn't have a key for this generation
                continue;
            }

            var keyGenProto = new KeyGenerationProto
            {
                KeyGeneration = kg.KeyGeneration,
                EncryptedKey = userEncryptedKey,
                ValidFromBlock = kg.ValidFromBlock.Value
            };

            // ValidToBlock is optional - only set if there's a newer KeyGeneration
            // For the last key, ValidToBlock remains unset (null)
            response.KeyGenerations.Add(keyGenProto);
        }

        _logger.LogInformation($"[GetKeyGenerations] Returning {response.KeyGenerations.Count} key generations");
        return response;
    }

    // ===== Group Feed Info Operation =====

    public override async Task<GetGroupFeedResponse> GetGroupFeed(
        GetGroupFeedRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation($"[GetGroupFeed] FeedId: {request.FeedId}");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);

            if (groupFeed == null)
            {
                return new GetGroupFeedResponse
                {
                    Success = false,
                    Message = "Group feed not found"
                };
            }

            // Auto-generate invite code for public groups if not present
            var inviteCode = groupFeed.InviteCode;
            if (groupFeed.IsPublic && string.IsNullOrEmpty(inviteCode))
            {
                inviteCode = await this._feedsStorageService.GenerateInviteCodeAsync(feedId);
                _logger.LogInformation($"[GetGroupFeed] Generated invite code: {inviteCode}");
            }

            var response = new GetGroupFeedResponse
            {
                Success = true,
                FeedId = groupFeed.FeedId.ToString(),
                Title = groupFeed.Title,
                Description = groupFeed.Description ?? "",
                IsPublic = groupFeed.IsPublic
            };

            // Only include invite code for public groups
            if (groupFeed.IsPublic && !string.IsNullOrEmpty(inviteCode))
            {
                response.InviteCode = inviteCode;
            }

            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetGroupFeed] ERROR");
            return new GetGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    public override async Task<GetGroupFeedByInviteCodeResponse> GetGroupFeedByInviteCode(
        GetGroupFeedByInviteCodeRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation($"[GetGroupFeedByInviteCode] InviteCode: {request.InviteCode}");

        try
        {
            if (string.IsNullOrWhiteSpace(request.InviteCode))
            {
                return new GetGroupFeedByInviteCodeResponse
                {
                    Success = false,
                    Message = "Invite code is required"
                };
            }

            var groupFeed = await this._feedsStorageService.GetGroupFeedByInviteCodeAsync(request.InviteCode);

            if (groupFeed == null)
            {
                return new GetGroupFeedByInviteCodeResponse
                {
                    Success = false,
                    Message = "No public group found with this invite code"
                };
            }

            // Count active members
            var activeParticipants = await this._feedsStorageService.GetActiveParticipantsAsync(groupFeed.FeedId);

            return new GetGroupFeedByInviteCodeResponse
            {
                Success = true,
                FeedId = groupFeed.FeedId.ToString(),
                Title = groupFeed.Title,
                Description = groupFeed.Description ?? "",
                IsPublic = groupFeed.IsPublic,
                MemberCount = activeParticipants.Count
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[GetGroupFeedByInviteCode] ERROR");
            return new GetGroupFeedByInviteCodeResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    // ===== Search Public Groups =====

    public override async Task<SearchPublicGroupsResponse> SearchPublicGroups(
        SearchPublicGroupsRequest request,
        ServerCallContext context)
    {
        _logger.LogInformation($"[SearchPublicGroups] Query: '{request.SearchQuery}', MaxResults: {request.MaxResults}");

        try
        {
            var maxResults = request.MaxResults > 0 ? request.MaxResults : 20;
            var groups = await this._feedsStorageService.SearchPublicGroupsAsync(
                request.SearchQuery ?? string.Empty,
                maxResults);

            var response = new SearchPublicGroupsResponse
            {
                Success = true,
                Message = $"Found {groups.Count} public groups"
            };

            foreach (var group in groups)
            {
                // Count active members for each group
                var activeParticipants = await this._feedsStorageService.GetActiveParticipantsAsync(group.FeedId);

                response.Groups.Add(new PublicGroupInfoProto
                {
                    FeedId = group.FeedId.ToString(),
                    Title = group.Title,
                    Description = group.Description ?? string.Empty,
                    MemberCount = activeParticipants.Count,
                    CreatedAtBlock = group.CreatedAtBlock.Value
                });
            }

            _logger.LogInformation($"[SearchPublicGroups] Returning {response.Groups.Count} groups");
            return response;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[SearchPublicGroups] ERROR");
            return new SearchPublicGroupsResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }
}


