using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public class UpdateIdentityContentHandler(
    ICredentialsProvider credentialProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;

    public bool CanValidate(Guid transactionKind) =>
        UpdateIdentityPayloadHandler.UpdateIdentityPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var updateIdentityPayload = transaction as SignedTransaction<UpdateIdentityPayload>;

        if (updateIdentityPayload == null)
        {
            return null;
        }

        var blockProducerCredentials = this._credentialProvider.GetCredentials();

        var signedByValidationTransaction = updateIdentityPayload.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);

        return signedByValidationTransaction;
    }
}
