using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Elections;

public interface ICreateElectionDraftValidationService
{
    bool IsValid(CreateElectionDraftPayload payload, string signatory);
}

public class CreateElectionDraftValidationService(
    IUnitOfWorkProvider<ElectionsDbContext> unitOfWorkProvider) : ICreateElectionDraftValidationService
{
    private readonly IUnitOfWorkProvider<ElectionsDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public bool IsValid(CreateElectionDraftPayload payload, string signatory)
    {
        if (!string.Equals(signatory, payload.OwnerPublicAddress, StringComparison.Ordinal))
        {
            return false;
        }

        var validationErrors = ElectionDraftValidator.ValidateDraftRequest(
            signatory,
            payload.SnapshotReason,
            payload.Draft);
        if (validationErrors.Count > 0)
        {
            return false;
        }

        using var unitOfWork = _unitOfWorkProvider.CreateReadOnly();
        var repository = unitOfWork.GetRepository<IElectionsRepository>();
        var existingElection = repository.GetElectionAsync(payload.ElectionId).GetAwaiter().GetResult();
        return existingElection is null;
    }
}
