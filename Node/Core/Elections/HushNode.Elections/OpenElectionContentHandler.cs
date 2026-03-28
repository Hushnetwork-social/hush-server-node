using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class OpenElectionContentHandler(
    ICredentialsProvider credentialProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    IElectionLifecycleService electionLifecycleService) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;

    public bool CanValidate(Guid transactionKind) =>
        OpenElectionPayloadHandler.OpenElectionPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<OpenElectionPayload>;
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
            || election.LifecycleState != ElectionLifecycleState.Draft
            || election.GovernanceMode != ElectionGovernanceMode.AdminOnly
            || !string.Equals(election.OwnerPublicAddress, signatory, StringComparison.Ordinal))
        {
            return null;
        }

        var readiness = _electionLifecycleService
            .EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
                signedTransaction.Payload.ElectionId,
                signedTransaction.Payload.RequiredWarningCodes))
            .GetAwaiter()
            .GetResult();
        if (!readiness.IsReadyToOpen)
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
