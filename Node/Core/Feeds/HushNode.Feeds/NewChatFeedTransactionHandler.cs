using HushNode.Caching;
using HushNode.Credentials;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using HushNode.Identity.Storage;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using HushShared.Identity.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

public class NewChatFeedTransactionHandler(
    IFeedsStorageService feedsStorageService,
    IBlockchainCache blockchainCache,
    IEventAggregator eventAggregator,
    IUserFeedsCacheService userFeedsCacheService,
    IFeedMetadataCacheService feedMetadataCacheService,
    IIdentityService identityService,
    ILogger<NewChatFeedTransactionHandler> logger)
    : INewChatFeedTransactionHandler
{
    private readonly IFeedsStorageService _feedsStorageService = feedsStorageService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly IUserFeedsCacheService _userFeedsCacheService = userFeedsCacheService;
    private readonly IFeedMetadataCacheService _feedMetadataCacheService = feedMetadataCacheService;
    private readonly IIdentityService _identityService = identityService;
    private readonly ILogger<NewChatFeedTransactionHandler> _logger = logger;

    public async Task HandleNewChatFeedTransactionAsync(ValidatedTransaction<NewChatFeedPayload> newChatFeedTransaction)
    {
        var newChatFeedPayload = newChatFeedTransaction.Payload;
        var currentBlock = this._blockchainCache.LastBlockIndex;

        var chatFeed = new Feed(
            newChatFeedPayload.FeedId,
            string.Empty,
            newChatFeedPayload.FeedType,
            currentBlock);

        if (newChatFeedPayload.FeedParticipants.Length != 2)
        {
            throw new InvalidOperationException("Cannot create a Chat Feed with less or more than 2 participants.");
        }

        var participantAddresses = new List<string>();

        foreach(var feedParticipant in newChatFeedPayload.FeedParticipants)
        {
            var participant = new FeedParticipant
            (
                newChatFeedPayload.FeedId,
                feedParticipant.ParticipantPublicAddress,
                ParticipantType.Owner,
                feedParticipant.EncryptedFeedKey
            )
            {
                Feed = chatFeed
            };

            chatFeed.Participants.Add(participant);
            participantAddresses.Add(feedParticipant.ParticipantPublicAddress);
        }

        await this._feedsStorageService.CreateFeed(chatFeed);

        // Publish event for other modules (e.g., Reactions) to handle
        // Fire and forget - don't block feed creation on reaction setup
        _ = this._eventAggregator.PublishAsync(new FeedCreatedEvent(
            newChatFeedPayload.FeedId,
            participantAddresses.ToArray(),
            newChatFeedPayload.FeedType));

        // FEAT-065: Resolve display names and populate full feed metadata for both participants
        // Chat feeds have user-specific titles: each participant sees the OTHER's display name
        var addr0 = participantAddresses[0];
        var addr1 = participantAddresses[1];
        var name0 = await ResolveDisplayNameAsync(addr0);
        var name1 = await ResolveDisplayNameAsync(addr1);

        foreach (var participantAddress in participantAddresses)
        {
            // Update feed list cache (FEAT-049)
            _ = this._userFeedsCacheService.AddFeedToUserCacheAsync(participantAddress, newChatFeedPayload.FeedId);

            // FEAT-065: Populate feed_meta with full metadata (title = other participant's name)
            var title = participantAddress == addr0 ? name1 : name0;
            var entry = new FeedMetadataEntry
            {
                Title = title,
                Type = (int)FeedType.Chat,
                LastBlockIndex = currentBlock.Value,
                Participants = participantAddresses.ToList(),
                CreatedAtBlock = currentBlock.Value,
                CurrentKeyGeneration = null
            };
            _ = this._feedMetadataCacheService.SetFeedMetadataAsync(
                participantAddress, newChatFeedPayload.FeedId, entry);
        }
    }

    private async Task<string> ResolveDisplayNameAsync(string publicAddress)
    {
        try
        {
            var identity = await _identityService.RetrieveIdentityAsync(publicAddress);
            if (identity is Profile profile)
                return profile.Alias;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve display name for {Address}", publicAddress);
        }

        return publicAddress.Length > 10
            ? publicAddress.Substring(0, 10) + "..."
            : publicAddress;
    }
}
