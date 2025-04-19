using HushShared.Identity.Model;
using Olimpo.EntityFramework.Persistency;

namespace HushNode.Identity.Storage;

public class IdentityStorageService(
    IUnitOfWorkProvider<IdentityDbContext> unitOfWorkProvider) 
    : IIdentityStorageService
{
    private readonly IUnitOfWorkProvider<IdentityDbContext> _unitOfWorkProvider = unitOfWorkProvider;

    public async Task<ProfileBase> RetrieveIdentityAsync(string publicSigingAddress)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();
        return await readOnlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .GetIdentityAsync(publicSigingAddress);
    }

    public async Task<IEnumerable<Profile>> SearchByDisplayNameAsync(string PartialDisplayName)
    {
        using var readOnlyUnitOfWork = this._unitOfWorkProvider.CreateReadOnly();

        return await readOnlyUnitOfWork
            .GetRepository<IIdentityRepository>()
            .SearchByDisplayNameAsync(PartialDisplayName);
    }
}
