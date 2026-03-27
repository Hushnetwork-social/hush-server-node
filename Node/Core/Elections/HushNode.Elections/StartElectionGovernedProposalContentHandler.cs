using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class StartElectionGovernedProposalContentHandler(
    ICredentialsProvider credentialProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider,
    IElectionLifecycleService electionLifecycleService) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;

    public bool CanValidate(Guid transactionKind) =>
        StartElectionGovernedProposalPayloadHandler.StartElectionGovernedProposalPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<StartElectionGovernedProposalPayload>;
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
            || election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold
            || !string.Equals(election.OwnerPublicAddress, signatory, StringComparison.Ordinal))
        {
            return null;
        }

        var existingProposal = repository.GetGovernedProposalAsync(signedTransaction.Payload.ProposalId).GetAwaiter().GetResult();
        if (existingProposal is not null)
        {
            return null;
        }

        var pendingProposal = repository.GetPendingGovernedProposalAsync(signedTransaction.Payload.ElectionId).GetAwaiter().GetResult();
        if (pendingProposal is not null)
        {
            return null;
        }

        var isValid = signedTransaction.Payload.ActionType switch
        {
            ElectionGovernedActionType.Open => _electionLifecycleService
                .EvaluateOpenReadinessAsync(new EvaluateElectionOpenReadinessRequest(
                    signedTransaction.Payload.ElectionId,
                    election.AcknowledgedWarningCodes))
                .GetAwaiter()
                .GetResult()
                .IsReadyToOpen,
            ElectionGovernedActionType.Close => election.LifecycleState == ElectionLifecycleState.Open,
            ElectionGovernedActionType.Finalize =>
                election.LifecycleState == ElectionLifecycleState.Closed && election.TallyReadyAt.HasValue,
            _ => false,
        };

        if (!isValid)
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }
}
