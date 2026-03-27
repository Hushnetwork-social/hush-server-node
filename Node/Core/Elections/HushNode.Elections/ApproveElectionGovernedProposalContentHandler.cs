using HushNode.Credentials;
using HushNode.Elections.Storage;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public class ApproveElectionGovernedProposalContentHandler(
    ICredentialsProvider credentialProvider,
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : ITransactionContentHandler
{
    private readonly ICredentialsProvider _credentialProvider = credentialProvider;
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public bool CanValidate(Guid transactionKind) =>
        ApproveElectionGovernedProposalPayloadHandler.ApproveElectionGovernedProposalPayloadKind == transactionKind;

    public AbstractTransaction? ValidateAndSign(AbstractTransaction transaction)
    {
        var signedTransaction = transaction as SignedTransaction<ApproveElectionGovernedProposalPayload>;
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
        var proposal = repository.GetGovernedProposalAsync(signedTransaction.Payload.ProposalId).GetAwaiter().GetResult();
        if (election is null
            || proposal is null
            || proposal.ElectionId != signedTransaction.Payload.ElectionId
            || election.GovernanceMode != ElectionGovernanceMode.TrusteeThreshold
            || proposal.ExecutionStatus != ElectionGovernedProposalExecutionStatus.WaitingForApprovals)
        {
            return null;
        }

        var governanceTrustees = ResolveGovernanceApproverRoster(repository, election, proposal);
        if (!governanceTrustees.Any(x =>
                string.Equals(x.TrusteeUserAddress, signatory, StringComparison.Ordinal)))
        {
            return null;
        }

        var existingApproval = repository.GetGovernedProposalApprovalAsync(proposal.Id, signatory).GetAwaiter().GetResult();
        if (existingApproval is not null)
        {
            return null;
        }

        var blockProducerCredentials = _credentialProvider.GetCredentials();
        return signedTransaction.SignByValidator(
            blockProducerCredentials.PublicSigningAddress,
            blockProducerCredentials.PrivateSigningKey);
    }

    private static IReadOnlyList<ElectionTrusteeReference> ResolveGovernanceApproverRoster(
        IElectionsRepository repository,
        ElectionRecord election,
        ElectionGovernedProposalRecord proposal)
    {
        if (proposal.ActionType == ElectionGovernedActionType.Open)
        {
            var activeVersion = repository.GetActiveCeremonyVersionAsync(election.ElectionId).GetAwaiter().GetResult();
            if (activeVersion is not null)
            {
                var trusteeStates = repository.GetCeremonyTrusteeStatesAsync(activeVersion.Id).GetAwaiter().GetResult();
                return activeVersion.BoundTrustees
                    .Where(boundTrustee => trusteeStates.Any(x =>
                        string.Equals(x.TrusteeUserAddress, boundTrustee.TrusteeUserAddress, StringComparison.OrdinalIgnoreCase) &&
                        x.State == ElectionTrusteeCeremonyState.CeremonyCompleted))
                    .ToArray();
            }
        }

        if (election.OpenArtifactId.HasValue)
        {
            var boundaryArtifacts = repository.GetBoundaryArtifactsAsync(election.ElectionId).GetAwaiter().GetResult();
            var openArtifact = boundaryArtifacts.FirstOrDefault(x =>
                x.Id == election.OpenArtifactId.Value || x.ArtifactType == ElectionBoundaryArtifactType.Open);
            if (openArtifact?.TrusteeSnapshot is not null)
            {
                return openArtifact.TrusteeSnapshot.AcceptedTrustees;
            }
        }

        var invitations = repository.GetTrusteeInvitationsAsync(election.ElectionId).GetAwaiter().GetResult();
        return invitations
            .Where(x => x.Status == ElectionTrusteeInvitationStatus.Accepted)
            .Select(x => new ElectionTrusteeReference(x.TrusteeUserAddress, x.TrusteeDisplayName))
            .ToArray();
    }
}
