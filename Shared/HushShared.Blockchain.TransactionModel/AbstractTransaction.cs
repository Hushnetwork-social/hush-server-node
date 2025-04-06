using System.Text.Json.Serialization;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.Converters;

namespace HushShared.Blockchain.TransactionModel;

[JsonConverter(typeof(AbstractTransactionConverter))]
public abstract record AbstractTransaction(
    TransactionId TransactionId, 
    Guid PayloadKind, 
    Timestamp TransactionTimeStamp)
{
    public abstract string ToJson();
}
