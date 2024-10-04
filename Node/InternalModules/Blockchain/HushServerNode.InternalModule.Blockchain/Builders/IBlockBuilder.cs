using HushEcosystem.Model.Blockchain;

namespace HushServerNode.InternalModule.Blockchain.Builders;

public interface IBlockBuilder
{
    IBlockBuilder WithBlockId(string blockId);

    IBlockBuilder WithPreviousBlockId(string previousBlockId);

    IBlockBuilder WithNextBlockId(string nextBlockId);

    IBlockBuilder WithBlockIndex(long blockIndex);

    IBlockBuilder WithRewardBeneficiary(string publicSigningAddress, string privateSigningKey, double blockHeight);

    IBlockBuilder WithGenesisSettings(string publicSigningAddress, string privateSigningKey, double blockHeight);

    IBlockBuilder WithTransactions(IEnumerable<VerifiedTransaction> verifiedTransactions);

    Block Build();
}