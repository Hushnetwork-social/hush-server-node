using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class FinalizeElectionContentHandler(
    ICredentialsProvider credentialProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public bool CanValidate(Guid transactionKind) =>
        FinalizeElectionPayloadHandler.FinalizeElectionPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<FinalizeElectionPayload>;
        if (signedTransaction is null)
        {
            return null;
        }

        var signatory = signedTransaction.UserSignature?.Signatory;
        if (!string.Equals(signatory, signedTransaction.Payload.ActorPublicAddress, StringComparison.Ordinal)
            || string.IsNullOrWhiteSpace(signatory))
        {
            return null;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var election = repository.GetElectionAsync(signedTransaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (election is null
            || election.LifecycleState != ElectionLifecycleState.Closed
            || !election.TallyReadyAt.HasValue
            || election.GovernanceMode != ElectionGovernanceMode.AdminOnly
            || !string.Equals(election.OwnerPublicAddress, signatory, StringComparison.Ordinal))
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
