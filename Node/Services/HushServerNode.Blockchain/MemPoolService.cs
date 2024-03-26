using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Builders;
using HushEcosystem.Model.GlobalEvents;
using HushServerNode.ApplicationSettings;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.Blockchain;

public class MemPoolService: 
    IMemPoolService,
    IHandle<NewFeedRequestedEvent>,
    IHandle<SendFeedMessageRequestedEvent>,
    IHandle<UserProfileRequestedEvent>
{
    private readonly IBlockchainService _blockchainService;
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly TransactionBaseConverter _transactionBaseConverter;
    private readonly IEventAggregator _eventAggregator;
    private readonly ILogger<MemPoolService> _logger;

    private ConcurrentBag<VerifiedTransaction> _nextBlockTransactionsCandidate;

    // private IList<TransactionBase> _allBlockTransactionsCandidate;

    public MemPoolService(
        IBlockchainService blockchainService,
        IApplicationSettingsService applicationSettingsService,
        TransactionBaseConverter transactionBaseConverter,
        IEventAggregator eventAggregator,
        ILogger<MemPoolService> logger)
    {
        this._blockchainService = blockchainService;
        this._applicationSettingsService = applicationSettingsService;
        this._transactionBaseConverter = transactionBaseConverter;
        this._eventAggregator = eventAggregator;
        this._logger = logger;

        this._eventAggregator.Subscribe(this);

        this._nextBlockTransactionsCandidate = new ConcurrentBag<VerifiedTransaction>();
    }
            
    public Task InitializeMemPool()
    {        
        // this._allBlockTransactionsCandidate = new List<TransactionBase>();

        return Task.CompletedTask;
    }

    public IEnumerable<VerifiedTransaction> GetNextBlockTransactionsCandidate()
    {
        return this._nextBlockTransactionsCandidate.TakeAndRemove(1000);
    }

    public void Handle(NewFeedRequestedEvent message)
    {
        // TODO [AboimPinto] Verify if the transaction is valid before add to the MemPool
        // ...

        var refuseMessage = string.Empty;

        if (message.NewFeedRequest.Feed.FeedType == FeedTypeEnum.Personal)
        {
            var anyPersonalFeed = this._blockchainService
                .ListTransactionsForAddress(message.NewFeedRequest.Feed.FeedParticipantPublicAddress, 0)
                .Any(x => x.SpecificTransaction.Id == Feed.TypeCode && ((Feed)x.SpecificTransaction).FeedType == FeedTypeEnum.Personal);

            if (anyPersonalFeed)
            {
                refuseMessage = $"The request for a personal feed for the user {message.NewFeedRequest.Feed.Issuer} was rejected because has already a personal feed associated.";
            }
        }
        else
        {
            // TODO [AboimPinto] How to verify a NewFeedTransaction when is not personal
            // TODO [AboimPinto] should these checks be here or in a strategy for this type of Request? 
        }

        
        if (string.IsNullOrEmpty(refuseMessage))
        {    
            // Add the valid transaction to the MemPool 
            var verifiedTransaction = new VerifiedTransaction
            {
                SpecificTransaction = message.NewFeedRequest.Feed,
                ValidatorAddress = this._applicationSettingsService.StackerInfo.PublicSigningAddress,
                BlockIndex = this._blockchainService.CurrentBlockIndex
            };

            var hashJsonOptions = new JsonSerializerOptionsBuilder()
                .WithTransactionBaseConverter(this._transactionBaseConverter)
                .WithModifierExcludeSignature()
                .WithModifierExcludeBlockIndex()
                .WithModifierExcludeHash()
                .Build();

            var signJsonOptions = new JsonSerializerOptionsBuilder()
                .WithTransactionBaseConverter(this._transactionBaseConverter)
                .WithModifierExcludeSignature()
                .WithModifierExcludeBlockIndex()
                .Build();

            verifiedTransaction.HashObject(hashJsonOptions);
            verifiedTransaction.Sign(this._applicationSettingsService.StackerInfo.PrivateSigningKey, signJsonOptions);

            this._nextBlockTransactionsCandidate.Add(verifiedTransaction);
        }
    }

    public void Handle(SendFeedMessageRequestedEvent message)
    {
        // TODO [AboimPinto] Verify if the Issuer can public message in the Feed

        var verifiedTransaction = new VerifiedTransaction
        {
            SpecificTransaction = message.SendMessageRequest.FeedMessage,
            ValidatorAddress = this._applicationSettingsService.StackerInfo.PublicSigningAddress,
            BlockIndex = this._blockchainService.CurrentBlockIndex
        };

        var hashJsonOptions = new JsonSerializerOptionsBuilder()
            .WithTransactionBaseConverter(this._transactionBaseConverter)
            .WithModifierExcludeSignature()
            .WithModifierExcludeBlockIndex()
            .WithModifierExcludeHash()
            .Build();

        var signJsonOptions = new JsonSerializerOptionsBuilder()
            .WithTransactionBaseConverter(this._transactionBaseConverter)
            .WithModifierExcludeSignature()
            .WithModifierExcludeBlockIndex()
            .Build();

        verifiedTransaction.HashObject(hashJsonOptions);
        verifiedTransaction.Sign(this._applicationSettingsService.StackerInfo.PrivateSigningKey, signJsonOptions);

        this._nextBlockTransactionsCandidate.Add(verifiedTransaction);
    }

    public void Handle(UserProfileRequestedEvent message)
    {
        var verifiedTransaction = new VerifiedTransaction
        {
            SpecificTransaction = message.UserProfileRequest.UserProfile,
            ValidatorAddress = this._applicationSettingsService.StackerInfo.PublicSigningAddress,
            BlockIndex = this._blockchainService.CurrentBlockIndex
        };
        
        var hashJsonOptions = new JsonSerializerOptionsBuilder()
            .WithTransactionBaseConverter(this._transactionBaseConverter)
            .WithModifierExcludeSignature()
            .WithModifierExcludeBlockIndex()
            .WithModifierExcludeHash()
            .Build();

        var signJsonOptions = new JsonSerializerOptionsBuilder()
            .WithTransactionBaseConverter(this._transactionBaseConverter)
            .WithModifierExcludeSignature()
            .WithModifierExcludeBlockIndex()
            .Build();

        verifiedTransaction.HashObject(hashJsonOptions);
        verifiedTransaction.Sign(this._applicationSettingsService.StackerInfo.PrivateSigningKey, signJsonOptions);

        this._nextBlockTransactionsCandidate.Add(verifiedTransaction);
    }
}
