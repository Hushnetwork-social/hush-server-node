using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds.gRPC;

public class InnerCircleApplicationService(
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService,
    IBlockchainCache blockchainCache,
    IFeedParticipantsCacheService feedParticipantsCacheService,
    IUserFeedsCacheService userFeedsCacheService,
    IGroupMembersCacheService groupMembersCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    ILogger<InnerCircleApplicationService> logger)
    : IInnerCircleApplicationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IFeedParticipantsCacheService _feedParticipantsCacheService = feedParticipantsCacheService;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IGroupMembersCacheService _groupMembersCacheService = groupMembersCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly ILogger<InnerCircleApplicationService> _logger = logger;

    private const int MaxMembersPerRotation = 512;
    private const int MaxInnerCircleMembersPerRequest = 100;

    public async Task<GetInnerCircleResponse> GetInnerCircleAsync(string ownerPublicAddress)
    {
        var owner = ownerPublicAddress?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(owner))
        {
            return new GetInnerCircleResponse
            {
                Success = false,
                Exists = false,
                Message = "OwnerPublicAddress is required"
            };
        }

        var innerCircle = await this._feedsStorageService.GetInnerCircleByOwnerAsync(owner);
        if (innerCircle == null || innerCircle.IsDeleted)
        {
            return new GetInnerCircleResponse
            {
                Success = true,
                Exists = false,
                Message = "Inner Circle not found"
            };
        }

        return new GetInnerCircleResponse
        {
            Success = true,
            Exists = true,
            FeedId = innerCircle.FeedId.ToString(),
            Message = "Inner Circle found"
        };
    }

    public async Task<CreateInnerCircleResponse> CreateInnerCircleAsync(string ownerPublicAddress, string requesterPublicAddress)
    {
        var owner = ownerPublicAddress?.Trim() ?? string.Empty;
        var requester = requesterPublicAddress?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(owner))
        {
            return new CreateInnerCircleResponse
            {
                Success = false,
                Message = "OwnerPublicAddress is required",
                ErrorCode = "INNER_CIRCLE_INVALID_MEMBERS"
            };
        }

        if (string.IsNullOrWhiteSpace(requester) || !string.Equals(requester, owner, StringComparison.Ordinal))
        {
            return new CreateInnerCircleResponse
            {
                Success = false,
                Message = "Only the profile owner can create the Inner Circle",
                ErrorCode = "INNER_CIRCLE_UNAUTHORIZED"
            };
        }

        var existingInnerCircle = await this._feedsStorageService.GetInnerCircleByOwnerAsync(owner);
        if (existingInnerCircle != null && !existingInnerCircle.IsDeleted)
        {
            return new CreateInnerCircleResponse
            {
                Success = true,
                FeedId = existingInnerCircle.FeedId.ToString(),
                Message = "Inner Circle already exists",
                ErrorCode = "INNER_CIRCLE_ALREADY_EXISTS"
            };
        }

        var ownerIdentity = await this._identityStorageService.RetrieveIdentityAsync(owner);
        if (ownerIdentity is not Profile ownerProfile || string.IsNullOrWhiteSpace(ownerProfile.PublicEncryptAddress))
        {
            return new CreateInnerCircleResponse
            {
                Success = false,
                Message = "Owner identity does not have a valid public encryption key",
                ErrorCode = "INNER_CIRCLE_INVALID_MEMBERS"
            };
        }

        var currentBlock = this._blockchainCache.LastBlockIndex;
        var feedId = FeedId.NewFeedId;
        var encryptedOwnerKey = EncryptKeys.Encrypt(EncryptKeys.GenerateAesKey(), ownerProfile.PublicEncryptAddress);

        var groupFeed = new GroupFeed(
            FeedId: feedId,
            Title: "Inner Circle",
            Description: "Auto-managed inner circle",
            IsPublic: false,
            CreatedAtBlock: currentBlock,
            CurrentKeyGeneration: 0,
            IsInnerCircle: true,
            OwnerPublicAddress: owner);

        var keyGeneration = new GroupFeedKeyGenerationEntity(
            feedId,
            KeyGeneration: 0,
            currentBlock,
            RotationTrigger.Join)
        {
            GroupFeed = groupFeed
        };

        groupFeed.KeyGenerations.Add(keyGeneration);

        var ownerParticipant = new GroupFeedParticipantEntity(
            feedId,
            owner,
            ParticipantType.Owner,
            currentBlock,
            LeftAtBlock: null,
            LastLeaveBlock: null)
        {
            GroupFeed = groupFeed
        };

        groupFeed.Participants.Add(ownerParticipant);

        keyGeneration.EncryptedKeys.Add(new GroupFeedEncryptedKeyEntity(
            feedId,
            KeyGeneration: 0,
            MemberPublicAddress: owner,
            EncryptedAesKey: encryptedOwnerKey)
        {
            KeyGenerationEntity = keyGeneration
        });

        await this._feedsStorageService.CreateGroupFeed(groupFeed);
        await this._userFeedsCacheService.AddFeedToUserCacheAsync(owner, feedId);

        _ = this._feedMetadataCacheService.SetFeedMetadataAsync(
            owner,
            feedId,
            new FeedMetadataEntry
            {
                Title = "Inner Circle",
                Type = (int)FeedType.Group,
                LastBlockIndex = currentBlock.Value,
                Participants = new List<string> { owner },
                CreatedAtBlock = currentBlock.Value,
                CurrentKeyGeneration = 0
            });

        return new CreateInnerCircleResponse
        {
            Success = true,
            FeedId = feedId.ToString(),
            Message = "Inner Circle created successfully"
        };
    }

    public async Task<AddMembersToInnerCircleResponse> AddMembersToInnerCircleAsync(
        string ownerPublicAddress,
        string requesterPublicAddress,
        IReadOnlyList<InnerCircleMemberProto> members)
    {
        var response = new AddMembersToInnerCircleResponse();
        var owner = ownerPublicAddress?.Trim() ?? string.Empty;
        var requester = requesterPublicAddress?.Trim() ?? string.Empty;

        if (string.IsNullOrWhiteSpace(owner))
        {
            response.Success = false;
            response.Message = "OwnerPublicAddress is required";
            response.ErrorCode = "INNER_CIRCLE_UNAUTHORIZED";
            return response;
        }

        if (string.IsNullOrWhiteSpace(requester) || !string.Equals(requester, owner, StringComparison.Ordinal))
        {
            response.Success = false;
            response.Message = "Only the profile owner can add members to the Inner Circle";
            response.ErrorCode = "INNER_CIRCLE_UNAUTHORIZED";
            return response;
        }

        if (members.Count == 0 || members.Count > MaxInnerCircleMembersPerRequest)
        {
            response.Success = false;
            response.Message = $"Members must contain between 1 and {MaxInnerCircleMembersPerRequest} users";
            response.ErrorCode = "INNER_CIRCLE_MEMBER_LIMIT_EXCEEDED";
            return response;
        }

        var innerCircle = await this._feedsStorageService.GetInnerCircleByOwnerAsync(owner);
        if (innerCircle == null || innerCircle.IsDeleted)
        {
            response.Success = false;
            response.Message = "Inner Circle not found";
            response.ErrorCode = "INNER_CIRCLE_NOT_FOUND";
            return response;
        }

        var duplicatesInPayload = members
            .Select(x => (x.PublicAddress ?? string.Empty).Trim())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .GroupBy(x => x, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToList();

        var normalizedMembers = members
            .GroupBy(x => (x.PublicAddress ?? string.Empty).Trim(), StringComparer.Ordinal)
            .Select(g => g.First())
            .ToList();

        var participantsToAdd = new List<GroupFeedParticipantEntity>();
        var participantsToRejoin = new List<string>();
        var joiningMemberEncryptKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var invalidMembers = new List<string>();
        var duplicateMembers = new HashSet<string>(duplicatesInPayload, StringComparer.Ordinal);
        var hasMalformedMemberEntry = false;
        var currentBlock = this._blockchainCache.LastBlockIndex;

        foreach (var member in normalizedMembers)
        {
            var memberAddress = (member.PublicAddress ?? string.Empty).Trim();
            var memberEncryptAddress = (member.PublicEncryptAddress ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(memberAddress) || string.IsNullOrWhiteSpace(memberEncryptAddress))
            {
                hasMalformedMemberEntry = true;
                if (!string.IsNullOrWhiteSpace(memberAddress))
                {
                    invalidMembers.Add(memberAddress);
                }

                continue;
            }

            var identity = await this._identityStorageService.RetrieveIdentityAsync(memberAddress);
            if (identity is not Profile profile ||
                string.IsNullOrWhiteSpace(profile.PublicEncryptAddress) ||
                !string.Equals(profile.PublicEncryptAddress, memberEncryptAddress, StringComparison.Ordinal))
            {
                invalidMembers.Add(memberAddress);
                continue;
            }

            var existingParticipant = await this._feedsStorageService
                .GetParticipantWithHistoryAsync(innerCircle.FeedId, memberAddress);

            if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
            {
                duplicateMembers.Add(memberAddress);
                continue;
            }

            if (existingParticipant != null && existingParticipant.LeftAtBlock != null)
            {
                participantsToRejoin.Add(memberAddress);
            }
            else
            {
                participantsToAdd.Add(new GroupFeedParticipantEntity(
                    innerCircle.FeedId,
                    memberAddress,
                    ParticipantType.Member,
                    currentBlock,
                    LeftAtBlock: null,
                    LastLeaveBlock: null));
            }

            joiningMemberEncryptKeys[memberAddress] = memberEncryptAddress;
        }

        response.DuplicateMembers.AddRange(duplicateMembers.OrderBy(x => x, StringComparer.Ordinal));
        response.InvalidMembers.AddRange(invalidMembers.Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal));

        if (response.DuplicateMembers.Count > 0 || response.InvalidMembers.Count > 0 || hasMalformedMemberEntry)
        {
            response.Success = false;
            response.Message = "Request contains duplicate or invalid members";
            response.ErrorCode = (response.InvalidMembers.Count > 0 || hasMalformedMemberEntry)
                ? "INNER_CIRCLE_INVALID_MEMBERS"
                : "INNER_CIRCLE_DUPLICATE_MEMBERS";
            return response;
        }

        var (rotationSuccess, keyGenerationEntity, rotationError) = await BuildInnerCircleKeyRotationEntityAsync(
            innerCircle.FeedId,
            joiningMemberEncryptKeys);

        if (!rotationSuccess || keyGenerationEntity == null)
        {
            response.Success = false;
            response.Message = $"Failed to rotate Inner Circle keys: {rotationError}";
            response.ErrorCode = "INNER_CIRCLE_ROTATION_FAILED";
            return response;
        }

        await this._feedsStorageService.ApplyInnerCircleMembershipAndKeyRotationAsync(
            innerCircle.FeedId,
            participantsToAdd,
            participantsToRejoin,
            currentBlock,
            keyGenerationEntity,
            currentBlock);

        await this._feedParticipantsCacheService.InvalidateKeyGenerationsAsync(innerCircle.FeedId);
        await this._groupMembersCacheService.InvalidateGroupMembersAsync(innerCircle.FeedId);

        foreach (var memberAddress in joiningMemberEncryptKeys.Keys)
        {
            await this._feedParticipantsCacheService.AddParticipantAsync(innerCircle.FeedId, memberAddress);
            await this._userFeedsCacheService.AddFeedToUserCacheAsync(memberAddress, innerCircle.FeedId);
        }

        response.Success = true;
        response.Message = "Members added to Inner Circle successfully";
        return response;
    }

    private async Task<(bool Success, GroupFeedKeyGenerationEntity? KeyGeneration, string? ErrorMessage)> BuildInnerCircleKeyRotationEntityAsync(
        FeedId feedId,
        IReadOnlyDictionary<string, string> joiningMemberEncryptKeys)
    {
        var currentMaxKeyGeneration = await this._feedsStorageService.GetMaxKeyGenerationAsync(feedId);
        if (currentMaxKeyGeneration == null)
        {
            return (false, null, $"Inner Circle {feedId} has no key generations.");
        }

        var newKeyGeneration = currentMaxKeyGeneration.Value + 1;
        var activeMemberAddresses = (await this._feedsStorageService.GetActiveGroupMemberAddressesAsync(feedId)).ToList();

        foreach (var joiningAddress in joiningMemberEncryptKeys.Keys)
        {
            if (!activeMemberAddresses.Contains(joiningAddress, StringComparer.Ordinal))
            {
                activeMemberAddresses.Add(joiningAddress);
            }
        }

        if (activeMemberAddresses.Count == 0)
        {
            return (false, null, "Cannot rotate keys for an empty Inner Circle.");
        }

        if (activeMemberAddresses.Count > MaxMembersPerRotation)
        {
            return (false, null, $"Inner Circle has {activeMemberAddresses.Count} members, exceeding maximum {MaxMembersPerRotation}.");
        }

        var plaintextAesKey = EncryptKeys.GenerateAesKey();
        var encryptedKeys = new List<GroupFeedEncryptedKeyEntity>(activeMemberAddresses.Count);

        try
        {
            foreach (var memberAddress in activeMemberAddresses)
            {
                string publicEncryptKey;

                if (joiningMemberEncryptKeys.TryGetValue(memberAddress, out var joiningMemberKey))
                {
                    publicEncryptKey = joiningMemberKey;
                }
                else
                {
                    var identity = await this._identityStorageService.RetrieveIdentityAsync(memberAddress);
                    if (identity is not Profile profile || string.IsNullOrWhiteSpace(profile.PublicEncryptAddress))
                    {
                        return (false, null, $"Could not retrieve a valid public encryption key for member {memberAddress}.");
                    }

                    publicEncryptKey = profile.PublicEncryptAddress;
                }

                string encryptedAesKey;
                try
                {
                    encryptedAesKey = EncryptKeys.Encrypt(plaintextAesKey, publicEncryptKey);
                }
                catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
                {
                    _logger.LogWarning(ex, "inner_circle.key_rotation.encryption_failed member={Member}", memberAddress);
                    return (false, null, $"Failed to encrypt key for member {memberAddress}: invalid public key format.");
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

        var keyGeneration = new GroupFeedKeyGenerationEntity(
            FeedId: feedId,
            KeyGeneration: newKeyGeneration,
            ValidFromBlock: this._blockchainCache.LastBlockIndex,
            RotationTrigger: RotationTrigger.Join)
        {
            EncryptedKeys = encryptedKeys
        };

        return (true, keyGeneration, null);
    }
}
