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

public class GroupMembershipApplicationService(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IIdentityStorageService identityStorageService,
    ILogger<GroupMembershipApplicationService> logger)
    : IGroupMembershipApplicationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly ILogger<GroupMembershipApplicationService> _logger = logger;

    private const int MaxMembersPerRotation = 512;

    public async Task<JoinGroupFeedResponse> JoinGroupFeedAsync(JoinGroupFeedRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var userAddress = request.JoiningUserPublicAddress ?? string.Empty;
        var userEncryptKey = request.HasJoiningUserPublicEncryptKey ? request.JoiningUserPublicEncryptKey : null;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new JoinGroupFeedResponse
            {
                Success = false,
                Message = "Group feed not found"
            };
        }

        if (groupFeed.IsDeleted)
        {
            return new JoinGroupFeedResponse
            {
                Success = false,
                Message = "This group has been deleted"
            };
        }

        if (!groupFeed.IsPublic)
        {
            return new JoinGroupFeedResponse
            {
                Success = false,
                Message = "Cannot join private group. An admin must add you."
            };
        }

        var existingParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, userAddress);
        if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
        {
            return new JoinGroupFeedResponse
            {
                Success = false,
                Message = "You are already a member of this group"
            };
        }

        if (existingParticipant?.LastLeaveBlock != null)
        {
            var currentBlock = this._blockchainCache.LastBlockIndex.Value;
            var cooldownEnd = existingParticipant.LastLeaveBlock.Value + 100;
            if (currentBlock < cooldownEnd)
            {
                return new JoinGroupFeedResponse
                {
                    Success = false,
                    Message = $"Cannot rejoin until block {cooldownEnd}. You left at block {existingParticipant.LastLeaveBlock.Value}."
                };
            }
        }

        var joinedAtBlock = this._blockchainCache.LastBlockIndex;

        if (existingParticipant != null)
        {
            await this._feedsStorageService.UpdateParticipantRejoinAsync(
                feedId,
                userAddress,
                joinedAtBlock,
                ParticipantType.Member);
        }
        else
        {
            var newParticipant = new GroupFeedParticipantEntity(
                feedId,
                userAddress,
                ParticipantType.Member,
                joinedAtBlock,
                LeftAtBlock: null,
                LastLeaveBlock: null);

            await this._feedsStorageService.AddParticipantAsync(feedId, newParticipant);
        }

        var (success, newKeyGen, errorMsg) = await TriggerKeyRotationAsync(
            feedId,
            RotationTrigger.Join,
            joiningMemberAddress: userAddress,
            leavingMemberAddress: null,
            joiningMemberPublicEncryptKey: userEncryptKey);

        if (!success)
        {
            return new JoinGroupFeedResponse
            {
                Success = false,
                Message = $"Joined group but key distribution failed: {errorMsg}"
            };
        }

        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, joinedAtBlock);
        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);

        return new JoinGroupFeedResponse
        {
            Success = true,
            Message = "Successfully joined the group"
        };
    }

    public async Task<LeaveGroupFeedResponse> LeaveGroupFeedAsync(LeaveGroupFeedRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var userAddress = request.LeavingUserPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new LeaveGroupFeedResponse
            {
                Success = false,
                Message = "Group not found"
            };
        }

        if (groupFeed.IsDeleted)
        {
            return new LeaveGroupFeedResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var participant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, userAddress);
        if (participant == null || participant.LeftAtBlock != null)
        {
            return new LeaveGroupFeedResponse
            {
                Success = false,
                Message = "You are not a member of this group"
            };
        }

        if (participant.ParticipantType == ParticipantType.Owner || participant.ParticipantType == ParticipantType.Admin)
        {
            var adminCount = await this._feedsStorageService.GetAdminCountAsync(feedId);
            if (adminCount <= 1)
            {
                return new LeaveGroupFeedResponse
                {
                    Success = false,
                    Message = "Cannot leave: you are the last admin. Promote another member first or delete the group."
                };
            }
        }

        var leftAtBlock = this._blockchainCache.LastBlockIndex;
        await this._feedsStorageService.UpdateParticipantLeaveStatusAsync(feedId, userAddress, leftAtBlock);

        var (success, _, errorMsg) = await TriggerKeyRotationAsync(
            feedId,
            RotationTrigger.Leave,
            leavingMemberAddress: userAddress);

        if (!success)
        {
            this._logger.LogWarning("[LeaveGroupFeed] Key rotation failed: {Reason}", errorMsg);
        }

        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, leftAtBlock);
        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);

        return new LeaveGroupFeedResponse
        {
            Success = true,
            Message = "Successfully left the group"
        };
    }

    public async Task<AddMemberToGroupFeedResponse> AddMemberToGroupFeedAsync(AddMemberToGroupFeedRequest request)
    {
        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var adminAddress = request.AdminPublicAddress ?? string.Empty;
        var newMemberAddress = request.NewMemberPublicAddress ?? string.Empty;

        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feedId);
        if (groupFeed == null)
        {
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = "Group not found"
            };
        }

        if (groupFeed.IsDeleted)
        {
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = "This group has been deleted. All actions are frozen."
            };
        }

        var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
        if (!isAdmin)
        {
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = "Only administrators can add members to the group"
            };
        }

        var existingParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, newMemberAddress);
        if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
        {
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = "User is already a member of this group"
            };
        }

        var currentBlock = this._blockchainCache.LastBlockIndex;

        if (existingParticipant != null)
        {
            await this._feedsStorageService.UpdateParticipantRejoinAsync(
                feedId,
                newMemberAddress,
                currentBlock,
                ParticipantType.Member);
        }
        else
        {
            var newParticipant = new GroupFeedParticipantEntity(
                feedId,
                newMemberAddress,
                ParticipantType.Member,
                currentBlock,
                LeftAtBlock: null,
                LastLeaveBlock: null);

            await this._feedsStorageService.AddParticipantAsync(feedId, newParticipant);
        }

        var (rotationSuccess, newKeyGeneration, errorMessage) = await TriggerKeyRotationAsync(
            feedId,
            RotationTrigger.Join,
            joiningMemberAddress: newMemberAddress);

        if (!rotationSuccess)
        {
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = $"Member was added but key distribution failed: {errorMessage}"
            };
        }

        await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);
        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(feedId);

        return new AddMemberToGroupFeedResponse
        {
            Success = true,
            Message = $"Member added successfully. New key generation: {newKeyGeneration}"
        };
    }

    private async Task<(bool Success, int? NewKeyGeneration, string? ErrorMessage)> TriggerKeyRotationAsync(
        FeedId feedId,
        RotationTrigger trigger,
        string? joiningMemberAddress = null,
        string? leavingMemberAddress = null,
        string? joiningMemberPublicEncryptKey = null)
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

        if (!string.IsNullOrEmpty(joiningMemberAddress))
        {
            if (!memberAddresses.Contains(joiningMemberAddress))
            {
                memberAddresses.Add(joiningMemberAddress);
            }
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
                string publicEncryptKey;

                if (!string.IsNullOrEmpty(joiningMemberPublicEncryptKey) &&
                    memberAddress == joiningMemberAddress)
                {
                    publicEncryptKey = joiningMemberPublicEncryptKey;
                }
                else
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

                    publicEncryptKey = fullProfile.PublicEncryptAddress;
                }

                string encryptedAesKey;
                try
                {
                    encryptedAesKey = EncryptKeys.Encrypt(plaintextAesKey, publicEncryptKey);
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
