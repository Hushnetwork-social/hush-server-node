using System.Collections.Generic;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.ApplicationSettings.Model;

namespace HushServerNode.Blockchain.Builders;

public class BlockBuilder : IBlockBuilder
{
    private string _blockId = string.Empty;
    private string _previousBlockId = string.Empty;
    private string _nextBlockId = string.Empty;
    private long _blockIndex;
    private TransactionBase _rewardTransaction;
    private VerifiedTransaction _verifiedRewardTransaction;
    private StackerInfo _stackerInfo;
    private IEnumerable<VerifiedTransaction> _verifiedTransactions;
    private readonly TransactionBaseConverter _transactionBaseConverter;

    public BlockBuilder(TransactionBaseConverter transactionBaseConverter)
    {
        this._transactionBaseConverter = transactionBaseConverter;
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

    public IBlockBuilder WithRewardBeneficiary(StackerInfo stackerInfo, double blockHeight)
    {
        this._rewardTransaction = new BlockCreationTransactionBuilder()
            .WithIssuerAddress(stackerInfo.PublicSigningAddress)
            .Build();

        this._rewardTransaction.HashObject(this._transactionBaseConverter);
        this._rewardTransaction.Sign(stackerInfo.PrivateSigningKey, this._transactionBaseConverter);
        
        this._verifiedRewardTransaction = new VerifiedTransaction
        {
            SpecificTransaction = this._rewardTransaction,
            ValidatorAddress = stackerInfo.PublicSigningAddress,
            BlockIndex = this._blockIndex
        };

        this._verifiedRewardTransaction.HashObject(this._transactionBaseConverter);
        this._verifiedRewardTransaction.Sign(stackerInfo.PrivateSigningKey, this._transactionBaseConverter);

        this._stackerInfo = stackerInfo;

        return this;
    }

    public IBlockBuilder WithTransactions(IEnumerable<VerifiedTransaction> verifiedTransactions)
    {
        this._verifiedTransactions = verifiedTransactions;
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

        newBlock.Sign(this._stackerInfo.PrivateSigningKey, this._transactionBaseConverter);

        return newBlock;
    }
}
