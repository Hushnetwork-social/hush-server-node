using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class FullIdentityContentHandler(
    ICredentialsProvider credentialProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;

    public bool CanValidate(Guid transactionKind)
    {
        return FullIdentityPayloadHandler.FullIdentityPayloadKind == transactionKind;
    }

    public AbstractTransaction ValidateAndSign(AbstractTransaction transaction)
    {
        var fullIdentityPayload = transaction as SignedTransaction<FullIdentityPayload>;

        if (fullIdentityPayload == null)
        {
            // throw new InvalidOperationException("Something went wrong and should never reach here. Some of the previous validation failed.");
            return null;    // <-- TODO [AboimPinto] Don't like to return null but for now works.
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = fullIdentityPayload.SignByValidator(
            blockProducerCredentials.PublicSigningAddress, 
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
