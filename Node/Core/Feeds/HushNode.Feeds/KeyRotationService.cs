using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Olimpo;

namespace HushNode.Feeds;

/// <summary>
/// Service for generating and distributing new encryption keys during Group Feed key rotations.
/// Generates AES-256 keys, encrypts them for each active member using ECIES, and creates
/// the key rotation transaction payload.
/// </summary>
public class KeyRotationService(
    IFeedsStorageService feedsStorageService,
    IIdentityStorageService identityStorageService,
    IBlockchainCache blockchainCache)
    : IKeyRotationService
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;

    /// <summary>Maximum number of members supported in a single key rotation.</summary>
    private const int MaxMembersPerRotation = 512;

    /// <inheritdoc/>
    public async Task<KeyRotationResult> TriggerRotationAsync(
        FeedId feedId,
        RotationTrigger trigger,
        string? joiningMemberAddress = null,
        string? leavingMemberAddress = null)
    {
        // Step 1: Get current max KeyGeneration
        var currentMaxKeyGeneration = await this._feedsStorageService.GetMaxKeyGenerationAsync(feedId);
        if (currentMaxKeyGeneration == null)
        {
            return KeyRotationResult.Failure($"Group feed {feedId} does not exist or has no KeyGenerations.");
        }

        var newKeyGeneration = currentMaxKeyGeneration.Value + 1;

        // Step 2: Get active member addresses (excludes Banned, includes Admin/Member/Blocked)
        var memberAddresses = (await this._feedsStorageService.GetActiveGroupMemberAddressesAsync(feedId)).ToList();

        // Adjust member list based on membership change
        if (!string.IsNullOrEmpty(leavingMemberAddress))
        {
            // For Leave/Ban: exclude the leaving/banned member
            memberAddresses = memberAddresses.Where(a => a != leavingMemberAddress).ToList();
        }

        if (!string.IsNullOrEmpty(joiningMemberAddress))
        {
            // For Join/Unban: include the joining member (might already be in list for Unban)
            if (!memberAddresses.Contains(joiningMemberAddress))
            {
                memberAddresses.Add(joiningMemberAddress);
            }
        }

        // Step 3: Validate member count
        if (memberAddresses.Count == 0)
        {
            return KeyRotationResult.Failure("Cannot rotate keys for a group with no active members.");
        }

        if (memberAddresses.Count > MaxMembersPerRotation)
        {
            return KeyRotationResult.Failure(
                $"Group has {memberAddresses.Count} members, exceeding the maximum of {MaxMembersPerRotation}.");
        }

        // Step 4: Generate new AES-256 key
        var plaintextAesKey = EncryptKeys.GenerateAesKey();

        // Step 5: Encrypt the AES key for each member using ECIES
        var encryptedKeys = new List<GroupFeedEncryptedKey>();
        try
        {
            foreach (var memberAddress in memberAddresses)
            {
                // Fetch the member's public encrypt key from Identity module
                var profile = await this._identityStorageService.RetrieveIdentityAsync(memberAddress);

                if (profile is NonExistingProfile || profile is not Profile fullProfile)
                {
                    return KeyRotationResult.Failure(
                        $"Could not retrieve identity for member {memberAddress}. Cannot complete key rotation.");
                }

                // Validate public encrypt key before attempting encryption
                if (string.IsNullOrEmpty(fullProfile.PublicEncryptAddress))
                {
                    return KeyRotationResult.Failure(
                        $"Member {memberAddress} has an empty public encrypt key. Cannot complete key rotation.");
                }

                // ECIES encrypt the AES key with the member's public encrypt key
                string encryptedAesKey;
                try
                {
                    encryptedAesKey = EncryptKeys.Encrypt(plaintextAesKey, fullProfile.PublicEncryptAddress);
                }
                catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentException)
                {
                    return KeyRotationResult.Failure(
                        $"ECIES encryption failed for member {memberAddress}: invalid public key format. Cannot complete key rotation.");
                }

                encryptedKeys.Add(new GroupFeedEncryptedKey(
                    MemberPublicAddress: memberAddress,
                    EncryptedAesKey: encryptedAesKey));
            }
        }
        finally
        {
            // Step 6: Security - zero the plaintext key from memory
            // Note: In .NET, strings are immutable and cannot be truly zeroed.
            // For production, consider using SecureString or byte arrays that can be cleared.
            // This is a best-effort cleanup.
            plaintextAesKey = null!;
        }

        // Step 7: Create the KeyRotation payload
        var validFromBlock = this._blockchainCache.LastBlockIndex.Value;
        var payload = new GroupFeedKeyRotationPayload(
            FeedId: feedId,
            NewKeyGeneration: newKeyGeneration,
            PreviousKeyGeneration: currentMaxKeyGeneration.Value,
            ValidFromBlock: validFromBlock,
            EncryptedKeys: encryptedKeys.ToArray(),
            RotationTrigger: trigger);

        return KeyRotationResult.Success(newKeyGeneration, payload);
    }

    /// <inheritdoc/>
    public async Task<KeyRotationResult> TriggerAndPersistRotationAsync(
        FeedId feedId,
        RotationTrigger trigger,
        string? joiningMemberAddress = null,
        string? leavingMemberAddress = null)
    {
        // Step 1: Generate the key rotation payload
        var result = await this.TriggerRotationAsync(feedId, trigger, joiningMemberAddress, leavingMemberAddress);

        if (!result.IsSuccess || result.Payload == null)
        {
            return result;
        }

        // Step 2: Convert payload to entity and persist
        var payload = result.Payload;
        var keyGenerationEntity = new GroupFeedKeyGenerationEntity(
            payload.FeedId,
            payload.NewKeyGeneration,
            new HushShared.Blockchain.BlockModel.BlockIndex(payload.ValidFromBlock),
            payload.RotationTrigger);

        // Map payload encrypted keys to entity encrypted keys
        foreach (var encryptedKey in payload.EncryptedKeys)
        {
            keyGenerationEntity.EncryptedKeys.Add(new GroupFeedEncryptedKeyEntity(
                payload.FeedId,
                payload.NewKeyGeneration,
                encryptedKey.MemberPublicAddress,
                encryptedKey.EncryptedAesKey));
        }

        // Step 3: Persist atomically
        await this._feedsStorageService.CreateKeyRotationAsync(keyGenerationEntity);

        return result;
    }
}
