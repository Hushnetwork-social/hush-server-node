using System.Text.Json;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.InternalModule.Blockchain.Cache;

namespace HushServerNode.InternalModule.Blockchain;

public static class BlockExtensions
{
    public static BlockEntity ToBlockEntity(this Block block, TransactionBaseConverter transactionBaseConverter)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            Converters = { transactionBaseConverter }
        };

        return new BlockEntity
        {
            BlockId = block.BlockId,
            Height = block.Index,
            PreviousBlockId = block.PreviousBlockId,
            NextBlockId = block.NextBlockId,
            Hash = block.Hash,
            BlockJson = block.ToJson(jsonOptions)
        };
    }

    public static string GetBlockGeneratorAddress(this IBlock block)
    {
        var verifiedRewardTransaction = block.Transactions.GetRewardTransaction();
        var blockGeneratorAddress = verifiedRewardTransaction.SpecificTransaction.Issuer;

        return blockGeneratorAddress;
    }  
}
