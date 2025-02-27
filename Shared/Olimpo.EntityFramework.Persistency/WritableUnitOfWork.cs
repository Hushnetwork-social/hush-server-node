using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;

namespace Olimpo.EntityFramework.Persistency;

public sealed class WritableUnitOfWork<TContext> : IWritableUnitOfWork<TContext> 
    where TContext : DbContext
{
    private readonly IServiceScope _serviceScope;
    private readonly IDbContextTransaction _transaction;
    
    public TContext Context { get; }

    public WritableUnitOfWork(IServiceProvider serviceProvider)     
    {
        _serviceScope = serviceProvider.CreateScope();
        Context = _serviceScope.ServiceProvider.GetRequiredService<TContext>();
        _transaction = Context.Database.BeginTransaction();
    }

    public TRepository GetRepository<TRepository>() 
        where TRepository : IRepository
    {
        var repo = _serviceScope.ServiceProvider.GetRequiredService<TRepository>();

        if (repo is IRepositoryWithContext<TContext> contextAwareRepo)
        {
            contextAwareRepo.SetContext(Context);
        }

        return repo;
    }

    public async Task CommitAsync()
    {
        await Context.SaveChangesAsync();
        await _transaction!.CommitAsync();
    }

    public async Task RollbackAsync()
    {
        await _transaction.RollbackAsync();
        Context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        _transaction?.Dispose();
        Context?.Dispose();
        _serviceScope?.Dispose();
    }
}