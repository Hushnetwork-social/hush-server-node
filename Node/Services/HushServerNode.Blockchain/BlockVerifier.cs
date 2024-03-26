using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Builders;
using HushServerNode.Blockchain.ExtensionMethods;

namespace HushServerNode.Blockchain;

public class BlockVerifier : IBlockVerifier
{
    private readonly TransactionBaseConverter _transactionBaseConverter;

    public BlockVerifier(TransactionBaseConverter transactionBaseConverter)
    {
        this._transactionBaseConverter = transactionBaseConverter;
    }

    public bool IsBlockValid(Block block)
    {
        var blockJsonOptions = new JsonSerializerOptionsBuilder()
            .WithTransactionBaseConverter(this._transactionBaseConverter)
            .WithModifierExcludeSignature()
            .WithModifierExcludeBlockIndex()
            .Build();

        var blockGeneratorAddress = block.GetBlockGeneratorAddress();
        var blockChecked = block.CheckSignature(blockGeneratorAddress, blockJsonOptions);

        if (blockChecked)
        {
            // interate over the transactions and check the signature of each one.
            foreach(var transaction in block.Transactions)
            {
                var transactionJsonOptions = new JsonSerializerOptionsBuilder()
                    .WithTransactionBaseConverter(this._transactionBaseConverter)
                    .WithModifierExcludeBlockIndex()
                    .WithModifierExcludeSignature()
                    .Build();

                if (!transaction.CheckSignature(transaction.ValidatorAddress, transactionJsonOptions))
                {
                    blockChecked = false;
                    break;
                }
            }
        }

        return blockChecked;
    }
}
