using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using HushEcosystem;
using HushEcosystem.Model;
using HushEcosystem.Model.GlobalEvents;
using HushEcosystem.Model.Rpc.Blockchain;
using HushEcosystem.Model.Rpc.Feeds;
using HushEcosystem.Model.Rpc.Handshake;
using HushEcosystem.Model.Rpc.Profiles;
using HushEcosystem.Model.Rpc.Transactions;
using HushServerNode.Blockchain;
using HushServerNode.ServerService;
using Olimpo;

namespace HushServerNode.RpcManager;

public class Rpc :
    IRpc,
    IHandle<HandshakeRequestedEvent>,
    IHandle<BlockchainHeightRequestedEvent>,
    IHandle<TransactionsWithAddressRequestedEvent>,
    IHandle<BalanceByAddressRequestedEvent>,
    IHandle<SearchAccountByPublicKeyRequestedEvent>,
    IHandle<FeedsForAddressRequestedEvent>,
    IHandle<FeedMessagesForAddressRequestedEvent>
{
    private readonly ITcpServerService _tcpServerService;
    private readonly IBlockchainService _blockchainService;
    private readonly IBlockchainIndexDb _blockchainIndexDb;
    private readonly TransactionBaseConverter _transactionBaseConverter;
    private readonly IEventAggregator _eventAggregator;

    public Rpc(
        ITcpServerService tcpServerService,
        IBlockchainService blockchainService,
        IBlockchainIndexDb blockchainIndexDb,
        TransactionBaseConverter transactionBaseConverter,
        IEventAggregator eventAggregator)
    {
        this._tcpServerService = tcpServerService;
        this._blockchainService = blockchainService;
        this._blockchainIndexDb = blockchainIndexDb;
        this._transactionBaseConverter = transactionBaseConverter;
        this._eventAggregator = eventAggregator;

        this._eventAggregator.Subscribe(this);
    }

    public void Handle(HandshakeRequestedEvent message)
    {
        // TODO [AboimPinto] Implement the rules that will accept the Handshake Request or not.

        var handshakeResponse = new HandshakeResponseBuilder()
            .WithResult(true)
            .WithFailureReason(string.Empty)
            .Build();

        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { this._transactionBaseConverter }
        };

        this._tcpServerService
            .SendThroughChannel(message.ChannelId, handshakeResponse.ToJson(jsonOptions).Compress());
    }

    public void Handle(BlockchainHeightRequestedEvent message)
    {
        var height =  this._blockchainService.CurrentBlockIndex;

        var blockchainHeightResponse = new BlockchainHeightResponse
        { 
            Height = height 
        };

        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { this._transactionBaseConverter }
        };

        this._tcpServerService
            .SendThroughChannel(message.ChannelId, blockchainHeightResponse.ToJson(jsonOptions).Compress());
    }

    public void Handle(TransactionsWithAddressRequestedEvent message)
    {
        var transactions = this._blockchainService
            .ListTransactionsForAddress(message.TransationsWithAddressRequest.Address, message.TransationsWithAddressRequest.LastHeightSynched);

        var currentBlockHeight = this._blockchainService.CurrentBlockIndex;

        var transactionsWithAddressResponse = new TransactionsWithAddressResponseBuilder()
            .WithTransactions(transactions)
            .WithBlockHeightSyncPoint(currentBlockHeight)
            .Build();

        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { this._transactionBaseConverter }
        };

        this._tcpServerService
            .SendThroughChannel(
                message.ChannelId, 
                transactionsWithAddressResponse
                    .ToJson(jsonOptions)
                    .Compress());
    }

    public void Handle(BalanceByAddressRequestedEvent message)
    {
        var balance = this._blockchainService
            .GetBalanceForAddress(message.BalanceByAddressRequest.Address);

        var balanceByAddressResponse = new BalanceByAddressResponseBuilder()
            .WithBalance(balance)
            .Build();

        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { this._transactionBaseConverter }
        };

        this._tcpServerService
            .SendThroughChannel(message.ChannelId, balanceByAddressResponse.ToJson(jsonOptions).Compress());
    }

    public void Handle(SearchAccountByPublicKeyRequestedEvent message)
    {
        SearchAccountByPublicKeyResponse searchAccountByPublicKeyResponse;

        if (string.IsNullOrEmpty(message.SearchAccountByPublicKeyRequest.UserPublicKey))
        {
            searchAccountByPublicKeyResponse = new SearchAccountByPublicKeyResponseBuilder()
                .WithFailureReason("Profile user public key is empty")
                .Build();
        }
        else
        {
            var userProfile = this._blockchainService.GetUserProfile(message.SearchAccountByPublicKeyRequest.UserPublicKey);

            if (userProfile == null)
            {
                searchAccountByPublicKeyResponse = new SearchAccountByPublicKeyResponseBuilder()
                    .WithFailureReason("User not found")
                    .Build();
            }
            else
            {
                searchAccountByPublicKeyResponse = new SearchAccountByPublicKeyResponseBuilder()
                    .WithUserProfile(userProfile)
                    .Build();
            }   
        }

        this._tcpServerService
                .SendThroughChannel(
                    message.ChannelId, 
                    searchAccountByPublicKeyResponse
                        .ToJson(new JsonSerializerOptions { Converters = { this._transactionBaseConverter } })
                        .Compress());
    }

    public void Handle(FeedsForAddressRequestedEvent message)
    {
        var userHasFeeds = this._blockchainIndexDb.FeedsOfParticipant.ContainsKey(message.FeedsForAddressRequest.Address);

        var response = new FeedsForAddressResponse();
        var feedsResponse = new List<IFeedDefinition>();
        if (userHasFeeds)
        {
            var feedIdsForUser = this._blockchainIndexDb.FeedsOfParticipant[message.FeedsForAddressRequest.Address];

            // var feeds = this._blockchainIndexDb.Feeds.Where(x => x.Key)
            foreach(var feedId in feedIdsForUser)
            {
                // var feed = this._blockchainIndexDb.Feeds[feedId];
                // feedsResponse.Add(feed);
            }

        }
        else
        {
            response.FeedDefinitions = feedsResponse;
        }



        // var feeds = this._blockchainIndexDb.Feeds
        //     .Where(x => x.Key == message.FeedsForAddressRequest.Address)
        //     .SelectMany(x => x.Value)
        //     .Where(x => x.BlockIndex >= message.FeedsForAddressRequest.SinceBlockIndex);

        // var response = new FeedsForAddressResponse
        // {
        //     FeedDefinitions = feeds.ToList()
        // };

        this._tcpServerService
                .SendThroughChannel(
                    message.ChannelId, 
                    response
                        .ToJson(new JsonSerializerOptions { Converters = { this._transactionBaseConverter } })
                        .Compress());
    }

    public void Handle(FeedMessagesForAddressRequestedEvent message)
    {
        // var feedMessages = this._blockchainIndexDb.FeedMessages[message.FeedMessagesForAddressRequest.]
    }
}