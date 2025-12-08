using Grpc.Core;
using HushNetwork.proto;
using HushNode.Feeds.Storage;
using HushNode.Identity;
using HushShared.Blockchain.BlockModel;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;

namespace HushNode.Feeds.gRPC;

public class FeedsGrpcService(
    IFeedsStorageService feedsStorageService,
    IFeedMessageStorageService feedMessageStorageService,
    IIdentityService identityService) : HushFeed.HushFeedBase
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IFeedMessageStorageService _feedMessageStorageService = feedMessageStorageService;
    private readonly IIdentityService _identityService = identityService;

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

            var replyFeed = new GetFeedForAddressReply.Types.Feed
            {
                FeedId = feed.FeedId.ToString(),
                FeedTitle = feedAlias,
                FeedType = (int)feed.FeedType,
                BlockIndex = feed.BlockIndex.Value,
                
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
            Console.WriteLine($"[GetFeedMessagesForAddress] Starting for ProfilePublicKey: {request.ProfilePublicKey?.Substring(0, Math.Min(20, request.ProfilePublicKey?.Length ?? 0))}..., BlockIndex: {request.BlockIndex}");

            var blockIndex = BlockIndexHandler.CreateNew(request.BlockIndex);

            Console.WriteLine("[GetFeedMessagesForAddress] Retrieving feeds for address...");
            var lastFeedsFromAddress = await this._feedsStorageService
                .RetrieveFeedsForAddress(request.ProfilePublicKey, new BlockIndex(0));

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

                    reply.Messages.Add(
                        new GetFeedMessagesForAddressReply.Types.FeedMessage
                        {
                            FeedMessageId = feedMessage.FeedMessageId.ToString(),
                            FeedId = feedMessage.FeedId.ToString(),
                            MessageContent = feedMessage.MessageContent,
                            IssuerPublicAddress = feedMessage.IssuerPublicAddress,
                            BlockIndex = feedMessage.BlockIndex.Value,
                            IssuerName = issuerName,
                            TimeStamp = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime(DateTime.SpecifyKind(timestamp, DateTimeKind.Utc)),
                        });

                    Console.WriteLine($"[GetFeedMessagesForAddress] Message added successfully");
                }
            }

            Console.WriteLine($"[GetFeedMessagesForAddress] Returning {reply.Messages.Count} total messages");
            return reply;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[GetFeedMessagesForAddress] ERROR: {ex.GetType().Name}: {ex.Message}");
            Console.WriteLine($"[GetFeedMessagesForAddress] Stack trace: {ex.StackTrace}");
            throw;
        }
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
}
