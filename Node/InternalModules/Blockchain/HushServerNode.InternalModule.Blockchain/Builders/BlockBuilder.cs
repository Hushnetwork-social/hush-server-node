using HushEcosystem;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Bank.Builders;

namespace HushServerNode.InternalModule.Blockchain.Builders;

public class BlockBuilder : IBlockBuilder
{
    private string _blockId = string.Empty;
    private string _previousBlockId = string.Empty;
    private string _nextBlockId = string.Empty;
    private long _blockIndex;
    private string _privateSigningKey;
    private TransactionBase _rewardTransaction;
    private VerifiedTransaction _verifiedRewardTransaction;
    private List<VerifiedTransaction> _verifiedTransactions;
    private readonly IBlockchainDbAccess _blockchainDbAccess;
    private readonly TransactionBaseConverter _transactionBaseConverter;

    public BlockBuilder(IBlockchainDbAccess blockchainDbAccess, TransactionBaseConverter transactionBaseConverter)
    {
        this._blockchainDbAccess = blockchainDbAccess;
        this._transactionBaseConverter = transactionBaseConverter;

        this._verifiedTransactions = new List<VerifiedTransaction>();
    }

    public IBlockBuilder WithBlockId(string blockId)
    {
        this._blockId = blockId;
        return this;
    }
        
    public IBlockBuilder WithPreviousBlockId(string previousBlockId)
    {
        this._previousBlockId = previousBlockId;
        return this;
    }

    public IBlockBuilder WithNextBlockId(string nextBlockId)
    {
        this._nextBlockId = nextBlockId;
        return this;
    }

    public IBlockBuilder WithBlockIndex(long blockIndex)
    {
        this._blockIndex = blockIndex;
        return this;
    }

    public IBlockBuilder WithRewardBeneficiary(
        string publicSigningAddress,
        string privateSigningKey,
        double blockHeight)
    {
        this._privateSigningKey = privateSigningKey;

        string hushBlockReward = this._blockchainDbAccess.GetSettings("SYSTEM", "HUSH_REWARD");

        this._rewardTransaction = new RewardTransactionBuilder()
            .WithIssuerAddress(publicSigningAddress)
            .WithRewardValue(hushBlockReward.StringToDouble())
            .Build();

        this._rewardTransaction.HashObject(this._transactionBaseConverter);
        this._rewardTransaction.Sign(privateSigningKey, this._transactionBaseConverter);
        
        this._verifiedRewardTransaction = new VerifiedTransaction
        {
            SpecificTransaction = this._rewardTransaction,
            ValidatorAddress = publicSigningAddress,
            BlockIndex = this._blockIndex
        };

        this._verifiedRewardTransaction.HashObject(this._transactionBaseConverter);
        this._verifiedRewardTransaction.Sign(privateSigningKey, this._transactionBaseConverter);

        return this;
    }

    public IBlockBuilder WithTransactions(IEnumerable<VerifiedTransaction> verifiedTransactions)
    {
        this._verifiedTransactions.AddRange(verifiedTransactions);
        return this;
    }

    public IBlockBuilder WithGenesisSettings(
        string publicSigningAddress,
        string privateSigningKey,
        double blockHeight)
    {
        this._privateSigningKey = privateSigningKey;

        var genesisSettings = new SettingsBuilder()
            .WithSettingsId(Guid.NewGuid())
            .WithSetting("SYSTEM", "HUSH_REWARD", "0.5", this._blockIndex)
            .Build();

        genesisSettings.HashObject(this._transactionBaseConverter);
        genesisSettings.Sign(privateSigningKey, this._transactionBaseConverter);

        if (this._verifiedTransactions == null)
        {
            this._verifiedTransactions = new List<VerifiedTransaction>();
        }

        var verifiedGenesisSettings = new VerifiedTransaction
        {
            SpecificTransaction = genesisSettings,
            ValidatorAddress = publicSigningAddress,
            BlockIndex = this._blockIndex
        };

        verifiedGenesisSettings.HashObject(this._transactionBaseConverter);
        verifiedGenesisSettings.Sign(privateSigningKey, this._transactionBaseConverter);

        this._verifiedTransactions.Add(verifiedGenesisSettings);

        return this;
    }

    public Block Build()
    {
        var newBlock = new Block(
            this._blockId, 
            this._previousBlockId, 
            this._nextBlockId, 
            this._blockIndex);

        // Add the verified reward transaction
        newBlock.Transactions.Add(this._verifiedRewardTransaction);

        // Get validated transactions from the MemPool
        if (this._verifiedTransactions != null)
        {
            foreach (var item in this._verifiedTransactions)
            {
                newBlock.Transactions.Add(item);
            }
        }

        newBlock.FinalizeBlock();

        newBlock.Sign(this._privateSigningKey, this._transactionBaseConverter);

        return newBlock;
    }
}
