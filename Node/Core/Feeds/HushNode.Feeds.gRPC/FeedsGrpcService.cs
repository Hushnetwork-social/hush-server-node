using Grpc.Core;
using HushNetwork.proto;
using HushNode.Caching;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushNode.Identity.Storage;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using MessageReactionTallyModel = HushShared.Reactions.Model.MessageReactionTally;
using Google.Protobuf;
using Olimpo;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.gRPC;

public class FeedsGrpcService(
    IFeedsStorageService feedsStorageService,
    IFeedMessageStorageService feedMessageStorageService,
    IIdentityService identityService,
    IIdentityStorageService identityStorageService,
    IBlockchainCache blockchainCache,
    IUnitOfWorkProvider<ReactionsDbContext> reactionsUnitOfWorkProvider) : HushFeed.HushFeedBase
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IIdentityService _identityService = identityService;
    private readonly IIdentityStorageService _identityStorageService = identityStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IUnitOfWorkProvider<ReactionsDbContext> _reactionsUnitOfWorkProvider = reactionsUnitOfWorkProvider;

    /// <summary>Maximum number of members supported in a single key rotation.</summary>
    private const int MaxMembersPerRotation = 512;

    public override async Task<HasPersonalFeedReply> HasPersonalFeed(
        HasPersonalFeedRequest request, 
        ServerCallContext context)
    {
        var hasPersonalFeed = await this._feedsStorageService.HasPersonalFeed(request.PublicPublicKey);

        return new HasPersonalFeedReply
        {
            FeedAvailable = hasPersonalFeed
        };
    }

    public override async Task<IsFeedInBlockchainReply> IsFeedInBlockchain(IsFeedInBlockchainRequest request, ServerCallContext context)
    {
        var isFeedInBlockchain = await this._feedsStorageService
            .IsFeedIsBlockchain(FeedIdHandler.CreateFromString(request.FeedId));

        return new IsFeedInBlockchainReply
        {
            FeedAvailable = isFeedInBlockchain
        };
    }

    public override async Task<GetFeedForAddressReply> GetFeedsForAddress(GetFeedForAddressRequest request, ServerCallContext context)
    {
        var blockIndex = BlockIndexHandler
            .CreateNew(request.BlockIndex);

        var lastFeeds = await this._feedsStorageService
            .RetrieveFeedsForAddress(request.ProfilePublicKey, blockIndex);

        var reply = new GetFeedForAddressReply();
        foreach(var feed in lastFeeds)
        {
            // TODO [AboimPinto] Here tghe FeedTitle should be calculated
            // * PersonalFeed -> ProfileAlias + (YOU) / First 10 characters of the public address + (YOU)
            // * ChatFeed -> Other chat participant ProfileAlias

            var feedAlias = feed.FeedType switch
            {
                FeedType.Personal => await ExtractPersonalFeedAlias(feed),
                FeedType.Chat => await ExtractChatFeedAlias(feed, request.ProfilePublicKey),
                FeedType.Group => await ExtractGroupFeedAlias(feed),
                FeedType.Broadcast => await ExtractBroascastAlias(feed),
                _ => throw new InvalidOperationException($"the FeedTYype {feed.FeedType} is not supported.")
            };

            // Calculate effective BlockIndex: MAX of feed BlockIndex and all participants' profile BlockIndex
            // This ensures clients detect changes when any participant updates their identity
            var effectiveBlockIndex = await GetEffectiveBlockIndex(feed);

            var replyFeed = new GetFeedForAddressReply.Types.Feed
            {
                FeedId = feed.FeedId.ToString(),
                FeedTitle = feedAlias,
                FeedType = (int)feed.FeedType,
                BlockIndex = effectiveBlockIndex,

            };

            foreach(var participant in feed.Participants)
            {
                replyFeed.FeedParticipants.Add(new GetFeedForAddressReply.Types.FeedParticipant
                {
                    FeedId = participant.FeedId.ToString(),
                    ParticipantPublicAddress = participant.ParticipantPublicAddress,
                    ParticipantType = (int)participant.ParticipantType,
                    EncryptedFeedKey = participant.EncryptedFeedKey
                });
            }

            reply.Feeds.Add(replyFeed);
        }

        return reply;
    }

    public override async Task<GetFeedMessagesForAddressReply> GetFeedMessagesForAddress(GetFeedMessagesForAddressRequest request, ServerCallContext context)
    {
        try
        {
            Console.WriteLine($"[GetFeedMessagesForAddress] Starting for ProfilePublicKey: {request.ProfilePublicKey?.Substring(0, Math.Min(20, request.ProfilePublicKey?.Length ?? 0))}..., BlockIndex: {request.BlockIndex}, LastReactionTallyVersion: {request.LastReactionTallyVersion}");

            var blockIndex = BlockIndexHandler.CreateNew(request.BlockIndex);

            Console.WriteLine("[GetFeedMessagesForAddress] Retrieving feeds for address...");
            var lastFeedsFromAddress = await this._feedsStorageService
                .RetrieveFeedsForAddress(request.ProfilePublicKey ?? string.Empty, new BlockIndex(0));

            Console.WriteLine($"[GetFeedMessagesForAddress] Found {lastFeedsFromAddress.Count()} feeds");

            var reply = new GetFeedMessagesForAddressReply();

            foreach(var feed in lastFeedsFromAddress)
            {
                Console.WriteLine($"[GetFeedMessagesForAddress] Processing feed: {feed.FeedId}, Type: {feed.FeedType}");

                // Get the last messages from each feed
                var lastFeedMessages = await this._feedMessageStorageService
                    .RetrieveLastFeedMessagesForFeedAsync(feed.FeedId, blockIndex);

                Console.WriteLine($"[GetFeedMessagesForAddress] Found {lastFeedMessages.Count()} messages for feed {feed.FeedId}");

                foreach (var feedMessage in lastFeedMessages)
                {
                    Console.WriteLine($"[GetFeedMessagesForAddress] Processing message: {feedMessage.FeedMessageId}, Timestamp: {feedMessage.Timestamp?.Value}");

                    var issuerName = await this.ExtractDisplayName(feedMessage.IssuerPublicAddress);
                    Console.WriteLine($"[GetFeedMessagesForAddress] IssuerName resolved: {issuerName}");

                    // Handle potential null or invalid timestamp
                    var timestamp = feedMessage.Timestamp?.Value ?? DateTime.UtcNow;
                    Console.WriteLine($"[GetFeedMessagesForAddress] Timestamp to convert: {timestamp}, Kind: {timestamp.Kind}");

                    var feedMessageReply = new GetFeedMessagesForAddressReply.Types.FeedMessage
                    {
                        FeedMessageId = feedMessage.FeedMessageId.ToString(),
                        FeedId = feedMessage.FeedId.ToString(),
                        MessageContent = feedMessage.MessageContent,
                        IssuerPublicAddress = feedMessage.IssuerPublicAddress,
                        BlockIndex = feedMessage.BlockIndex.Value,
                        IssuerName = issuerName,
                        TimeStamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                    };

                    // Add AuthorCommitment if present (Protocol Omega)
                    if (feedMessage.AuthorCommitment != null)
                    {
                        feedMessageReply.AuthorCommitment = ByteString.CopyFrom(feedMessage.AuthorCommitment);
                    }

                    // Add ReplyToMessageId if present (Reply to Message feature)
                    if (feedMessage.ReplyToMessageId != null)
                    {
                        feedMessageReply.ReplyToMessageId = feedMessage.ReplyToMessageId.ToString();
                    }

                    // Add KeyGeneration if present (Group Feeds)
                    if (feedMessage.KeyGeneration != null)
                    {
                        feedMessageReply.KeyGeneration = feedMessage.KeyGeneration.Value;
                    }

                    reply.Messages.Add(feedMessageReply);

                    Console.WriteLine($"[GetFeedMessagesForAddress] Message added successfully");
                }
            }

            // Fetch and add reaction tallies (Protocol Omega)
            await AddReactionTallies(request, reply);

            Console.WriteLine($"[GetFeedMessagesForAddress] Returning {reply.Messages.Count} total messages, {reply.ReactionTallies.Count} reaction tallies");
            return reply;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetFeedMessagesForAddress] ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[GetFeedMessagesForAddress] Stack trace: {ex.StackTrace}");
            throw;
        }
    }

    private async Task AddReactionTallies(GetFeedMessagesForAddressRequest request, GetFeedMessagesForAddressReply reply)
    {
        try
        {
            // Get all feed IDs for this user
            var userFeedIds = await this._feedsStorageService
                .GetFeedIdsForUserAsync(request.ProfilePublicKey);

            if (userFeedIds.Count == 0)
            {
                reply.MaxReactionTallyVersion = request.LastReactionTallyVersion;
                return;
            }

            Console.WriteLine($"[GetFeedMessagesForAddress] Fetching reaction tallies for {userFeedIds.Count} feeds since version {request.LastReactionTallyVersion}");

            // Get updated tallies using unit of work pattern
            using var unitOfWork = this._reactionsUnitOfWorkProvider.CreateReadOnly();
            var reactionsRepo = unitOfWork.GetRepository<IReactionsRepository>();
            var reactionTallies = await reactionsRepo.GetTalliesForFeedsAsync(userFeedIds, request.LastReactionTallyVersion);

            Console.WriteLine($"[GetFeedMessagesForAddress] Found {reactionTallies.Count} updated reaction tallies");

            long maxVersion = request.LastReactionTallyVersion;

            foreach (var tally in reactionTallies)
            {
                reply.ReactionTallies.Add(MapTallyToProto(tally));
                maxVersion = Math.Max(maxVersion, tally.Version);
            }

            reply.MaxReactionTallyVersion = maxVersion;
        }
        catch (Exception ex)
        {
            // Don't fail the entire request if reactions fail - just log and continue
            Console.WriteLine($"[GetFeedMessagesForAddress] Warning: Failed to fetch reaction tallies: {ex.Message}");
            Console.WriteLine($"[GetFeedMessagesForAddress] Exception type: {ex.GetType().Name}");
            Console.WriteLine($"[GetFeedMessagesForAddress] Stack trace: {ex.StackTrace}");
            reply.MaxReactionTallyVersion = request.LastReactionTallyVersion;
        }
    }

    private static GetFeedMessagesForAddressReply.Types.MessageReactionTally MapTallyToProto(
        MessageReactionTallyModel tally)
    {
        var proto = new GetFeedMessagesForAddressReply.Types.MessageReactionTally
        {
            MessageId = tally.MessageId.ToString(),
            TallyVersion = tally.Version,
            ReactionCount = tally.TotalCount
        };

        // Map EC points (6 points for each array)
        for (int i = 0; i < 6; i++)
        {
            proto.TallyC1.Add(new ECPoint
            {
                X = ByteString.CopyFrom(tally.TallyC1X[i]),
                Y = ByteString.CopyFrom(tally.TallyC1Y[i])
            });
            proto.TallyC2.Add(new ECPoint
            {
                X = ByteString.CopyFrom(tally.TallyC2X[i]),
                Y = ByteString.CopyFrom(tally.TallyC2Y[i])
            });
        }

        return proto;
    }

    private Task<string> ExtractBroascastAlias(Feed feed)
    {
        throw new NotImplementedException();
    }

    private async Task<string> ExtractChatFeedAlias(Feed feed, string requesterPublicAddress)
    {
        var otherParticipantPublicAddress = feed.Participants
            .Single(x => x.ParticipantPublicAddress != requesterPublicAddress)
            .ParticipantPublicAddress;

        return await this.ExtractDisplayName(otherParticipantPublicAddress);
    }

    private async Task<string> ExtractPersonalFeedAlias(Feed feed)
    {
        var displayName = await this.ExtractDisplayName(feed.Participants.Single().ParticipantPublicAddress);

        return string.Format("{0} (YOU)", displayName);
    }

    private async Task<string> ExtractGroupFeedAlias(Feed feed)
    {
        var groupFeed = await this._feedsStorageService.GetGroupFeedAsync(feed.FeedId);

        if (groupFeed != null)
        {
            return groupFeed.Title;
        }

        // Fallback: use the alias from the Feed record if GroupFeed lookup fails
        return feed.Alias;
    }

    private async Task<string> ExtractDisplayName(string publicSigningAddress)
    {
        var identity = await this._identityService.RetrieveIdentityAsync(publicSigningAddress);

        if (identity is Profile profile)
        {
            return profile.Alias;
        }

        // Fallback for NonExistingProfile or unknown types - use first 10 chars of public address
        return publicSigningAddress.Length > 10
            ? publicSigningAddress.Substring(0, 10) + "..."
            : publicSigningAddress;
    }

    /// <summary>
    /// Calculate effective BlockIndex for a feed.
    /// Returns MAX of feed BlockIndex and all participants' profile BlockIndex.
    /// This ensures clients detect when any participant updates their identity.
    /// </summary>
    private async Task<long> GetEffectiveBlockIndex(Feed feed)
    {
        var maxBlockIndex = feed.BlockIndex.Value;

        foreach (var participant in feed.Participants)
        {
            var identity = await this._identityService.RetrieveIdentityAsync(participant.ParticipantPublicAddress);

            if (identity is Profile profile && profile.BlockIndex.Value > maxBlockIndex)
            {
                maxBlockIndex = profile.BlockIndex.Value;
            }
        }

        return maxBlockIndex;
    }

    // ===== Group Feed Query Operations (FEAT-017) =====

    public override async Task<GetGroupMembersResponse> GetGroupMembers(
        GetGroupMembersRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[GetGroupMembers] FeedId: {request.FeedId}");

        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var activeParticipants = await this._feedsStorageService.GetActiveParticipantsAsync(feedId);

        var response = new GetGroupMembersResponse();

        foreach (var participant in activeParticipants)
        {
            response.Members.Add(new GroupFeedMemberProto
            {
                PublicAddress = participant.ParticipantPublicAddress,
                ParticipantType = (int)participant.ParticipantType,
                JoinedAtBlock = participant.JoinedAtBlock.Value
            });
        }

        Console.WriteLine($"[GetGroupMembers] Returning {response.Members.Count} members");
        return response;
    }

    public override async Task<GetKeyGenerationsResponse> GetKeyGenerations(
        GetKeyGenerationsRequest request,
        ServerCallContext context)
    {
        var userAddress = request.UserPublicAddress ?? string.Empty;
        Console.WriteLine($"[GetKeyGenerations] FeedId: {request.FeedId}, UserAddress: {userAddress.Substring(0, Math.Min(10, userAddress.Length))}...");

        var feedId = FeedIdHandler.CreateFromString(request.FeedId);
        var keyGenerations = await this._feedsStorageService.GetKeyGenerationsForUserAsync(feedId, userAddress);

        var response = new GetKeyGenerationsResponse();

        foreach (var kg in keyGenerations)
        {
            // Find this user's encrypted key for this KeyGeneration
            var userEncryptedKey = kg.EncryptedKeys
                .FirstOrDefault(ek => ek.MemberPublicAddress == userAddress)
                ?.EncryptedAesKey ?? string.Empty;

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

        Console.WriteLine($"[GetKeyGenerations] Returning {response.KeyGenerations.Count} key generations");
        return response;
    }

    // ===== Group Feed Admin Operations (FEAT-017) =====

    public override async Task<AddMemberToGroupFeedResponse> AddMemberToGroupFeed(
        AddMemberToGroupFeedRequest request,
        ServerCallContext context)
    {
        Console.WriteLine($"[AddMemberToGroupFeed] FeedId: {request.FeedId}, Admin: {request.AdminPublicAddress?.Substring(0, Math.Min(10, request.AdminPublicAddress?.Length ?? 0))}..., NewMember: {request.NewMemberPublicAddress?.Substring(0, Math.Min(10, request.NewMemberPublicAddress?.Length ?? 0))}...");

        try
        {
            var feedId = FeedIdHandler.CreateFromString(request.FeedId);
            var adminAddress = request.AdminPublicAddress ?? string.Empty;
            var newMemberAddress = request.NewMemberPublicAddress ?? string.Empty;
            var newMemberEncryptKey = request.NewMemberPublicEncryptKey ?? string.Empty;

            // Step 1: Validate admin has permission
            var isAdmin = await this._feedsStorageService.IsAdminAsync(feedId, adminAddress);
            if (!isAdmin)
            {
                Console.WriteLine($"[AddMemberToGroupFeed] Failed: User is not an admin");
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = "Only administrators can add members to the group"
                };
            }

            // Step 2: Check if member is already in the group
            var existingParticipant = await this._feedsStorageService.GetParticipantWithHistoryAsync(feedId, newMemberAddress);
            if (existingParticipant != null && existingParticipant.LeftAtBlock == null)
            {
                Console.WriteLine($"[AddMemberToGroupFeed] Failed: Member already in group");
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = "User is already a member of this group"
                };
            }

            // Step 3: Get current block height for JoinedAtBlock
            var currentBlock = this._blockchainCache.LastBlockIndex;
            var joinedAtBlock = currentBlock;

            // Step 4: Add or update the participant
            if (existingParticipant != null)
            {
                // Participant existed before (re-joining after leaving) - update their status
                await this._feedsStorageService.UpdateParticipantRejoinAsync(
                    feedId,
                    newMemberAddress,
                    joinedAtBlock,
                    ParticipantType.Member);
                Console.WriteLine($"[AddMemberToGroupFeed] Updated existing participant for rejoin");
            }
            else
            {
                // New participant - create new record
                var newParticipant = new GroupFeedParticipantEntity(
                    feedId,
                    newMemberAddress,
                    ParticipantType.Member,
                    joinedAtBlock,
                    LeftAtBlock: null,
                    LastLeaveBlock: null);

                await this._feedsStorageService.AddParticipantAsync(feedId, newParticipant);
                Console.WriteLine($"[AddMemberToGroupFeed] Added new participant");
            }

            // Step 5: Trigger key rotation to include the new member
            Console.WriteLine($"[AddMemberToGroupFeed] Triggering key rotation for new member");
            var rotationResult = await TriggerKeyRotationAsync(
                feedId,
                RotationTrigger.Join,
                joiningMemberAddress: newMemberAddress);
            var (rotationSuccess, newKeyGeneration, errorMessage) = rotationResult;

            if (!rotationSuccess)
            {
                Console.WriteLine($"[AddMemberToGroupFeed] Key rotation failed: {errorMessage}");
                // Note: Member was added but key rotation failed. This is a partial failure.
                // The member won't be able to decrypt messages until key rotation succeeds.
                return new AddMemberToGroupFeedResponse
                {
                    Success = false,
                    Message = $"Member was added but key distribution failed: {errorMessage}"
                };
            }

            // Step 6: Update the feed's BlockIndex to signal other clients that there's a change
            // This ensures existing group members will sync the new KeyGeneration
            Console.WriteLine($"[AddMemberToGroupFeed] Updating feed BlockIndex to trigger client sync");
            await this._feedsStorageService.UpdateFeedBlockIndexAsync(feedId, currentBlock);

            Console.WriteLine($"[AddMemberToGroupFeed] Success - new KeyGeneration: {newKeyGeneration}");
            return new AddMemberToGroupFeedResponse
            {
                Success = true,
                Message = $"Member added successfully. New key generation: {newKeyGeneration}"
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[AddMemberToGroupFeed] ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[AddMemberToGroupFeed] Stack trace: {ex.StackTrace}");
            return new AddMemberToGroupFeedResponse
            {
                Success = false,
                Message = $"Internal error: {ex.Message}"
            };
        }
    }

    /// <summary>
    /// Triggers a key rotation for a Group Feed when a member joins.
    /// Generates a new AES key, encrypts it for all current active members, and stores the key generation.
    /// </summary>
    private async Task<(bool Success, int? NewKeyGeneration, string? ErrorMessage)> TriggerKeyRotationAsync(
        FeedId feedId,
        RotationTrigger trigger,
        string? joiningMemberAddress = null,
        string? leavingMemberAddress = null)
    {
        // Step 1: Get current max KeyGeneration
        var currentMaxKeyGeneration = await this._feedsStorageService.GetMaxKeyGenerationAsync(feedId);
        if (currentMaxKeyGeneration == null)
        {
            return (false, null, $"Group feed {feedId} does not exist or has no KeyGenerations.");
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
            return (false, null, "Cannot rotate keys for a group with no active members.");
        }

        if (memberAddresses.Count > MaxMembersPerRotation)
        {
            return (false, null, $"Group has {memberAddresses.Count} members, exceeding the maximum of {MaxMembersPerRotation}.");
        }

        // Step 4: Generate new AES-256 key
        var plaintextAesKey = EncryptKeys.GenerateAesKey();

        // Step 5: Encrypt the AES key for each member using ECIES
        var encryptedKeys = new List<GroupFeedEncryptedKeyEntity>();
        try
        {
            foreach (var memberAddress in memberAddresses)
            {
                // Fetch the member's public encrypt key from Identity module
                var profile = await this._identityStorageService.RetrieveIdentityAsync(memberAddress);

                if (profile is NonExistingProfile || profile is not Profile fullProfile)
                {
                    return (false, null, $"Could not retrieve identity for member {memberAddress}. Cannot complete key rotation.");
                }

                // Validate public encrypt key before attempting encryption
                if (string.IsNullOrEmpty(fullProfile.PublicEncryptAddress))
                {
                    return (false, null, $"Member {memberAddress} has an empty public encrypt key. Cannot complete key rotation.");
                }

                // ECIES encrypt the AES key with the member's public encrypt key
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
            // Step 6: Security - zero the plaintext key from memory
            // Note: In .NET, strings are immutable and cannot be truly zeroed.
            plaintextAesKey = null!;
        }

        // Step 7: Create and store the KeyGeneration
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
