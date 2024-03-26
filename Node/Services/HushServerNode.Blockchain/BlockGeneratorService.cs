using System;
using System.Reactive.Linq;
using System.Threading.Tasks;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Builders;
using HushEcosystem.Model.GlobalEvents;
using HushEcosystem.Model.Rpc.Profiles;
using HushServerNode.ApplicationSettings;
using HushServerNode.Blockchain.Builders;
using HushServerNode.Blockchain.Events;
using HushServerNode.Blockchain.Factories;
using Olimpo;

namespace HushServerNode.Blockchain;

public class BlockGeneratorService :
    IBlockGeneratorService,
    IHandleAsync<BlockchainInitializedEvent>
{
    private readonly IBlockBuilder _blockBuilder;
    private readonly IBlockchainService _blockchainService;
    private readonly IMemPoolService _memPoolService;
    private readonly IBlockCreatedEventFactory _blockCreatedEventFactory;
    private readonly TransactionBaseConverter _transactionBaseConverter;
    private readonly IApplicationSettingsService _applicationSettingsService;
    private readonly IEventAggregator _eventAggregator;

    private IObservable<long> _blockGeneratorLoop;

    public BlockGeneratorService(
        IBlockBuilder blockBuilder,
        IBlockchainService blockchainService,
        IMemPoolService memPoolService,
        IBlockCreatedEventFactory blockCreatedEventFactory,
        TransactionBaseConverter transactionBaseConverter,
        IApplicationSettingsService applicationSettingsService,
        IEventAggregator eventAggregator)
    {
        this._blockBuilder = blockBuilder;
        this._blockchainService = blockchainService;
        this._memPoolService = memPoolService;
        this._blockCreatedEventFactory = blockCreatedEventFactory;
        this._transactionBaseConverter = transactionBaseConverter;
        this._applicationSettingsService = applicationSettingsService;
        this._eventAggregator = eventAggregator;

        this._eventAggregator.Subscribe(this);

        this._blockGeneratorLoop = Observable.Interval(TimeSpan.FromSeconds(3));
    }

    public async Task HandleAsync(BlockchainInitializedEvent message)
    {
        // Create a profile for the Stacker
        var userProfile = new UserProfile
        {
            UserName ="AboimPinto Staker",
            Issuer = this._applicationSettingsService.StackerInfo.PublicSigningAddress,
            UserPublicSigningAddress = this._applicationSettingsService.StackerInfo.PublicSigningAddress,
            UserPublicEncryptAddress = this._applicationSettingsService.StackerInfo.PublicEncryptAddress,
            IsPublic = false
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

        userProfile.HashObject(hashJsonOptions);
        userProfile.Sign(this._applicationSettingsService.StackerInfo.PrivateSigningKey, signJsonOptions);
        // End create profile

        var userProfileRequestedEvent = new UserProfileRequestedEvent(string.Empty, new UserProfileRequest
        {
            UserProfile = userProfile
        });
        await this._eventAggregator.PublishAsync(userProfileRequestedEvent);

        this._blockGeneratorLoop.Subscribe(async x => 
        {
            var transactions = this._memPoolService.GetNextBlockTransactionsCandidate();

            var newBlockHeight = this._blockchainService.CurrentBlockIndex + 1; 

            // TODO [AboimPinto] Add the transactions to the block
            var block = this._blockBuilder
                .WithBlockIndex(newBlockHeight)
                .WithBlockId(this._blockchainService.CurrentNextBlockId)
                .WithPreviousBlockId(this._blockchainService.CurrentBlockId)
                .WithNextBlockId(Guid.NewGuid().ToString())
                .WithRewardBeneficiary(this._applicationSettingsService.StackerInfo, newBlockHeight)
                .WithTransactions(transactions)
                .Build();

            await this._eventAggregator.PublishAsync(this._blockCreatedEventFactory.GetInstance(block));
        });
    }
}
