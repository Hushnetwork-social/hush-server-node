using HushNode.Credentials;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class CreateElectionDraftContentHandler(
    ICredentialsProvider credentialProvider,
    ICreateElectionDraftValidationService createElectionDraftValidationService) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly ICreateElectionDraftValidationService _createElectionDraftValidationService =
        createElectionDraftValidationService;

    public bool CanValidate(Guid transactionKind) =>
        CreateElectionDraftPayloadHandler.CreateElectionDraftPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<CreateElectionDraftPayload>;
        if (signedTransaction is null)
        {
            return null;
        }

        var signatory = signedTransaction.UserSignature?.Signatory;
        if (string.IsNullOrWhiteSpace(signatory))
        {
            return null;
        }

        if (!_createElectionDraftValidationService.IsValid(signedTransaction.Payload, signatory))
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
