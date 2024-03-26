using System.Collections.Generic;
using HushEcosystem.Model.Blockchain;
using HushServerNode.ApplicationSettings.Model;

namespace HushServerNode.Blockchain.Builders;

public interface IBlockBuilder
{
    IBlockBuilder WithBlockId(string blockId);

    IBlockBuilder WithPreviousBlockId(string previousBlockId);

    IBlockBuilder WithNextBlockId(string nextBlockId);

    IBlockBuilder WithBlockIndex(double blockIndex);

    IBlockBuilder WithRewardBeneficiary(StackerInfo stackerInfo, double blockHeight);

    IBlockBuilder WithTransactions(IEnumerable<VerifiedTransaction> verifiedTransactions);

    Block Build();
}