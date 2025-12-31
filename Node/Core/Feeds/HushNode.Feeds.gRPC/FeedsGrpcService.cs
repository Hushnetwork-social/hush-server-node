using Grpc.Core;
using HushNetwork.proto;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushNode.Reactions.Storage;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using MessageReactionTallyModel = HushShared.Reactions.Model.MessageReactionTally;
using Google.Protobuf;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Feeds.gRPC;

public class FeedsGrpcService(
    IFeedsStorageService feedsStorageService,
    IFeedMessageStorageService feedMessageStorageService,
    IIdentityService identityService,
    IUnitOfWorkProvider<ReactionsDbContext> reactionsUnitOfWorkProvider) : HushFeed.HushFeedBase
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IIdentityService _identityService = identityService;
    private readonly IUnitOfWorkProvider<ReactionsDbContext> _reactionsUnitOfWorkProvider = reactionsUnitOfWorkProvider;

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
}
