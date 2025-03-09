using System.Text.Json.Serialization;
using HushNode.Blockchain.Model.Transaction.Converters;

namespace HushNode.Blockchain.Model.Transaction;

[JsonConverter(typeof(AbstractTransactionConverter))]
public abstract record AbstractTransaction(
    TransactionId TransactionId, 
    Guid PayloadKind, 
    Timestamp TransactionTimeStamp)
{

    public abstract bool CheckValidatorSignature();

    public abstract bool CheckUserSignature();

    public abstract string ToJson();

    public abstract string CreateSignature(string privateKey);

}
