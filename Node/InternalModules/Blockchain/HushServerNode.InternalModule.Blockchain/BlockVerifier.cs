using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushEcosystem.Model.Builders;
using HushServerNode.InternalModule.Blockchain.ExtensionMethods;

namespace HushServerNode.InternalModule.Blockchain;

// TODO [AboimPinto]: For now, the verification of a Block is just this "small" method but in the future can be a full InternalModule.
// Verification os a block should be more then just check if the signature is valid.

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
