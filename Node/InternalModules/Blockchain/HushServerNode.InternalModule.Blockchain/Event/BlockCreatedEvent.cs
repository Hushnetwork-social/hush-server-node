using System.Text.Json;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;
using HushServerNode.Cache.Blockchain;

namespace HushServerNode.InternalModule.Blockchain.Events;

public class BlockCreatedEvent
{
    public Block Block { get; }

    public BlockchainState BlockchainState { get; }

    private readonly TransactionBaseConverter _transactionBaseConverter;

    public BlockCreatedEvent(
        Block blockSigned, 
        BlockchainState blockchainState,
        TransactionBaseConverter transactionBaseConverter)
    {
        this._transactionBaseConverter = transactionBaseConverter;
        this.Block = blockSigned;
        this.BlockchainState = blockchainState;
    }

    public override string ToString()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            Converters = { this._transactionBaseConverter }
        });
    }
}
