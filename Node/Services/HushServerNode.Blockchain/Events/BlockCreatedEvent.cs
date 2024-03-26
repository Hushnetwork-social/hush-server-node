using System.Text.Json;
using HushEcosystem.Model;
using HushEcosystem.Model.Blockchain;

namespace HushServerNode.Blockchain.Events
{
    public class BlockCreatedEvent
    {
        public Block Block { get; }
        private readonly TransactionBaseConverter _transactionBaseConverter;

        public BlockCreatedEvent(
            Block blockSigned, 
            TransactionBaseConverter transactionBaseConverter)
        {
            this._transactionBaseConverter = transactionBaseConverter;
            this.Block = blockSigned;
        }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this, new JsonSerializerOptions
            {
                Converters = { this._transactionBaseConverter }
            });
        }
    }
}