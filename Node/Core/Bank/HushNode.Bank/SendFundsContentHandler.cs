using HushNode.Credentials;
using HushShared.Bank.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushNode.Bank;

public class SendFundsContentHandler(
    ICredentialsProvider credentialProvider) 
    : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;

    public bool CanValidate(Guid transactionKind) => 
        SendFundsPayloadHandler.SendFundsPayloadKind == transactionKind;

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<SendFundsPayload>;

        if (signedTransaction == null)
        {
            // throw new InvalidOperationException("Something went wrong and should never reach here. Some of the previous validation failed.");
            return null;    // <-- TODO [AboimPinto] Don't like to return null but for now works.
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress, 
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
