using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds.gRPC;

public class GroupAdministrationApplicationService(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IIdentityStorageService identityStorageService,
    ILogger<GroupAdministrationApplicationService> logger)
    : IGroupAdministrationApplicationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly ILogger<GroupAdministrationApplicationService> _logger = logger;

    private const int MaxMembersPerRotation = 512;

    public async Task<BlockMemberResponse> BlockMemberAsync(BlockMemberRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var blockedUserAddress = request.BlockedUserPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new BlockMemberResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new BlockMemberResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new BlockMemberResponse
            {
                Success = false,
                Message = "Only administrators can block members"
            };
        }

        var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, blockedUserAddress);
        if (targetParticipant == null || targetParticipant.LeftAtBlock != null)
        {
            return new BlockMemberResponse
            {
                Success = false,
                Message = "User is not an active member of this group"
            };
        }

        if (targetParticipant.ParticipantType is ParticipantType.Admin or ParticipantType.Owner)
        {
            return new BlockMemberResponse
            {
                Success = false,
                Message = "Cannot block an administrator. Demote them first."
            };
        }

        await this._feedsStorageService.UpdateParticipantTypeAsync(
            feedId,
            blockedUserAddress,
            ParticipantType.Blocked);

        return new BlockMemberResponse
        {
            Success = true,
            Message = "Member blocked successfully"
        };
    }

    public async Task<UnblockMemberResponse> UnblockMemberAsync(UnblockMemberRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var unblockedUserAddress = request.UnblockedUserPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new UnblockMemberResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new UnblockMemberResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new UnblockMemberResponse
            {
                Success = false,
                Message = "Only administrators can unblock members"
            };
        }

        var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, unblockedUserAddress);
        if (targetParticipant == null || targetParticipant.ParticipantType != ParticipantType.Blocked)
        {
            return new UnblockMemberResponse
            {
                Success = false,
                Message = "User is not blocked in this group"
            };
        }

        await this._feedsStorageService.UpdateParticipantTypeAsync(
            feedId,
            unblockedUserAddress,
            ParticipantType.Member);

        return new UnblockMemberResponse
        {
            Success = true,
            Message = "Member unblocked successfully"
        };
    }

    public async Task<BanFromGroupFeedResponse> BanFromGroupFeedAsync(BanFromGroupFeedRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var bannedUserAddress = request.BannedUserPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new BanFromGroupFeedResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new BanFromGroupFeedResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new BanFromGroupFeedResponse
            {
                Success = false,
                Message = "Only administrators can ban members"
            };
        }

        var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, bannedUserAddress);
        if (targetParticipant == null || targetParticipant.LeftAtBlock != null)
        {
            return new BanFromGroupFeedResponse
            {
                Success = false,
                Message = "User is not an active member of this group"
            };
        }

        if (targetParticipant.ParticipantType is ParticipantType.Admin or ParticipantType.Owner)
        {
            return new BanFromGroupFeedResponse
            {
                Success = false,
                Message = "Cannot ban an administrator. Demote them first."
            };
        }

        var bannedAtBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateParticipantBanAsync(feedId, bannedUserAddress, bannedAtBlock);

        var (success, _, errorMsg) = await TriggerKeyRotationAsync(
            feedId,
            RotationTrigger.Ban,
            leavingMemberAddress: bannedUserAddress);

        if (!success)
        {
            this._logger.LogWarning("[BanFromGroupFeed] Key rotation failed: {Reason}", errorMsg);
        }

        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, bannedAtBlock);
        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);

        return new BanFromGroupFeedResponse
        {
            Success = true,
            Message = "Member banned successfully"
        };
    }

    public async Task<UnbanFromGroupFeedResponse> UnbanFromGroupFeedAsync(UnbanFromGroupFeedRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var unbannedUserAddress = request.UnbannedUserPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new UnbanFromGroupFeedResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new UnbanFromGroupFeedResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new UnbanFromGroupFeedResponse
            {
                Success = false,
                Message = "Only administrators can unban members"
            };
        }

        var isBanned = await this._feedsStorageService.IsBannedAsync(feedId, unbannedUserAddress);
        if (!isBanned)
        {
            return new UnbanFromGroupFeedResponse
            {
                Success = false,
                Message = "User is not banned from this group"
            };
        }

        var rejoinedAtBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateParticipantUnbanAsync(feedId, unbannedUserAddress, rejoinedAtBlock);

        var (success, _, errorMsg) = await TriggerKeyRotationAsync(
            feedId,
            RotationTrigger.Unban,
            joiningMemberAddress: unbannedUserAddress);

        if (!success)
        {
            return new UnbanFromGroupFeedResponse
            {
                Success = false,
                Message = $"Unbanned but key distribution failed: {errorMsg}"
            };
        }

        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, rejoinedAtBlock);
        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);

        return new UnbanFromGroupFeedResponse
        {
            Success = true,
            Message = "Member unbanned successfully"
        };
    }

    public async Task<PromoteToAdminResponse> PromoteToAdminAsync(PromoteToAdminRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var memberAddress = request.MemberPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new PromoteToAdminResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new PromoteToAdminResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new PromoteToAdminResponse
            {
                Success = false,
                Message = "Only administrators can promote members"
            };
        }

        var targetParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, memberAddress);
        if (targetParticipant == null || targetParticipant.LeftAtBlock != null)
        {
            return new PromoteToAdminResponse
            {
                Success = false,
                Message = "User is not an active member of this group"
            };
        }

        if (targetParticipant.ParticipantType is ParticipantType.Admin or ParticipantType.Owner)
        {
            return new PromoteToAdminResponse
            {
                Success = false,
                Message = "User is already an administrator"
            };
        }

        await this._feedsStorageService.UpdateParticipantTypeAsync(
            feedId,
            memberAddress,
            ParticipantType.Admin);

        var currentBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

        return new PromoteToAdminResponse
        {
            Success = true,
            Message = "Member promoted to administrator"
        };
    }

    public async Task<UpdateGroupFeedTitleResponse> UpdateGroupFeedTitleAsync(UpdateGroupFeedTitleRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var newTitle = request.NewTitle ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new UpdateGroupFeedTitleResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new UpdateGroupFeedTitleResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new UpdateGroupFeedTitleResponse
            {
                Success = false,
                Message = "Only administrators can update the group title"
            };
        }

        if (string.IsNullOrWhiteSpace(newTitle))
        {
            return new UpdateGroupFeedTitleResponse
            {
                Success = false,
                Message = "Title cannot be empty"
            };
        }

        if (newTitle.Length > 100)
        {
            return new UpdateGroupFeedTitleResponse
            {
                Success = false,
                Message = "Title cannot exceed 100 characters"
            };
        }

        await this._feedsStorageService.UpdateGroupFeedTitleAsync(feedId, newTitle);

        var currentBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

        try
        {
            var participantAddresses = await this._feedParticipantsCacheService.GetParticipantsAsync(feedId);

            if (participantAddresses != null)
            {
                foreach (var memberAddress in participantAddresses)
                {
                    _ = this._feedMetadataCacheService.UpdateFeedTitleAsync(memberAddress, feedId, newTitle);
                }
            }
            else
            {
                var dbParticipants = await this._feedsStorageService.GetAllParticipantsAsync(feedId);
                foreach (var participant in dbParticipants)
                {
                    if (participant.LeftAtBlock == null)
                    {
                        _ = this._feedMetadataCacheService.UpdateFeedTitleAsync(
                            participant.ParticipantPublicAddress, feedId, newTitle);
                    }
                }
            }
        }
        catch (Exception cascadeEx)
        {
            this._logger.LogWarning(cascadeEx,
                "Failed to cascade group title change to feed_meta caches for feed {FeedId}", feedId);
        }

        return new UpdateGroupFeedTitleResponse
        {
            Success = true,
            Message = "Group title updated successfully"
        };
    }

    public async Task<UpdateGroupFeedDescriptionResponse> UpdateGroupFeedDescriptionAsync(UpdateGroupFeedDescriptionRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var newDescription = request.NewDescription ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new UpdateGroupFeedDescriptionResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new UpdateGroupFeedDescriptionResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new UpdateGroupFeedDescriptionResponse
            {
                Success = false,
                Message = "Only administrators can update the group description"
            };
        }

        if (newDescription.Length > 500)
        {
            return new UpdateGroupFeedDescriptionResponse
            {
                Success = false,
                Message = "Description cannot exceed 500 characters"
            };
        }

        await this._feedsStorageService.UpdateGroupFeedDescriptionAsync(feedId, newDescription);

        var currentBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

        return new UpdateGroupFeedDescriptionResponse
        {
            Success = true,
            Message = "Group description updated successfully"
        };
    }

    public async Task<UpdateGroupFeedSettingsResponse> UpdateGroupFeedSettingsAsync(UpdateGroupFeedSettingsRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new UpdateGroupFeedSettingsResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new UpdateGroupFeedSettingsResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new UpdateGroupFeedSettingsResponse
            {
                Success = false,
                Message = "Only administrators can update group settings"
            };
        }

        string? newTitle = request.HasNewTitle ? request.NewTitle : null;
        string? newDescription = request.HasNewDescription ? request.NewDescription : null;
        bool? isPublic = request.HasIsPublic ? request.IsPublic : null;

        if (newTitle != null && (newTitle.Length < 1 || newTitle.Length > 100))
        {
            return new UpdateGroupFeedSettingsResponse
            {
                Success = false,
                Message = "Title must be between 1 and 100 characters"
            };
        }

        if (newDescription != null && newDescription.Length > 500)
        {
            return new UpdateGroupFeedSettingsResponse
            {
                Success = false,
                Message = "Description cannot exceed 500 characters"
            };
        }

        await this._feedsStorageService.UpdateGroupFeedSettingsAsync(feedId, newTitle, newDescription, isPublic);

        var currentBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

        return new UpdateGroupFeedSettingsResponse
        {
            Success = true,
            Message = "Group settings updated successfully"
        };
    }

    public async Task<DeleteGroupFeedResponse> DeleteGroupFeedAsync(DeleteGroupFeedRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new DeleteGroupFeedResponse { Success = false, Message = "Group not found" };
        }

        if (groupFeed.IsDeleted)
        {
            return new DeleteGroupFeedResponse
            {
                Success = false,
                Message = "Group has already been deleted"
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new DeleteGroupFeedResponse
            {
                Success = false,
                Message = "Only administrators can delete the group"
            };
        }

        await this._feedsStorageService.MarkGroupFeedDeletedAsync(feedId);

        var currentBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

        return new DeleteGroupFeedResponse
        {
            Success = true,
            Message = "Group deleted successfully"
        };
    }

    private async Task<(bool Success, int? NewKeyGeneration, string? ErrorMessage)> TriggerKeyRotationAsync(
        FeedId feedId,
        RotationTrigger trigger,
        string? joiningMemberAddress = null,
        string? leavingMemberAddress = null)
    {
        var currentMaxKeyGeneration = await this._feedsStorageService.GetMaxKeyGenerationAsync(feedId);
        if (currentMaxKeyGeneration == null)
        {
            return (false, null, $"Group feed {feedId} does not exist or has no KeyGenerations.");
        }

        var newKeyGeneration = currentMaxKeyGeneration.Value + 1;
        var memberAddresses = (await this._feedsStorageService.GetActiveGroupMemberAddressesAsync(feedId)).ToList();

        if (!string.IsNullOrEmpty(leavingMemberAddress))
        {
            memberAddresses = memberAddresses.Where(a => a != leavingMemberAddress).ToList();
        }

        if (!string.IsNullOrEmpty(joiningMemberAddress) && !memberAddresses.Contains(joiningMemberAddress))
        {
            memberAddresses.Add(joiningMemberAddress);
        }

        if (memberAddresses.Count == 0)
        {
            return (false, null, "Cannot rotate keys for a group with no active members.");
        }

        if (memberAddresses.Count > MaxMembersPerRotation)
        {
            return (false, null, $"Group has {memberAddresses.Count} members, exceeding the maximum of {MaxMembersPerRotation}.");
        }

        var plaintextAesKey = EncryptKeys.GenerateAesKey();
        var encryptedKeys = new List<GroupFeedEncryptedKeyEntity>();
        try
        {
            foreach (var memberAddress in memberAddresses)
            {
                var profile = await this._identityStorageService.RetrieveIdentityAsync(memberAddress);

                if (profile is NonExistingProfile || profile is not Profile fullProfile)
                {
                    return (false, null, $"Could not retrieve identity for member {memberAddress}. Cannot complete key rotation.");
                }

                if (string.IsNullOrEmpty(fullProfile.PublicEncryptAddress))
                {
                    return (false, null, $"Member {memberAddress} has an empty public encrypt key. Cannot complete key rotation.");
                }

                string encryptedAesKey;
                try
                {
                    encryptedAesKey = EncryptKeys.Encrypt(plaintextAesKey, fullProfile.PublicEncryptAddress);
                }
                catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
                {
                    return (false, null, $"ECIES encryption failed for member {memberAddress}: invalid public key format. Cannot complete key rotation.");
                }

                encryptedKeys.Add(new GroupFeedEncryptedKeyEntity(
                    FeedId: feedId,
                    KeyGeneration: newKeyGeneration,
                    MemberPublicAddress: memberAddress,
                    EncryptedAesKey: encryptedAesKey));
            }
        }
        finally
        {
            plaintextAesKey = null!;
        }

        var validFromBlock = this._blockchainCache.LastBlockIndex.Value;
        var keyGenerationEntity = new GroupFeedKeyGenerationEntity(
            FeedId: feedId,
            KeyGeneration: newKeyGeneration,
            ValidFromBlock: new BlockIndex(validFromBlock),
            RotationTrigger: trigger)
        {
            EncryptedKeys = encryptedKeys
        };

        await this._feedsStorageService.CreateKeyRotationAsync(keyGenerationEntity);
        return (true, newKeyGeneration, null);
    }
}
