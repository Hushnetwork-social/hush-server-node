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
        this._serviceScope = serviceProvider.CreateScope();
        this.Context = _serviceScope.ServiceProvider.GetRequiredService<TContext>();
        this._transaction = Context.Database.BeginTransaction();
    }

    public TRepository GetRepository<TRepository>() 
        where TRepository : IRepository
    {
        var repo = this._serviceScope.ServiceProvider.GetRequiredService<TRepository>();

        if (repo is IRepositoryWithContext<TContext> contextAwareRepo)
        {
            contextAwareRepo.SetContext(Context);
        }

        return repo;
    }

    public async Task CommitAsync()
    {
        await this.Context.SaveChangesAsync();
        await this._transaction!.CommitAsync();
    }

    public async Task RollbackAsync()
    {
        await this._transaction.RollbackAsync();
        this.Context.ChangeTracker.Clear();
    }

    public void Dispose()
    {
        this._transaction?.Dispose();
        this.Context?.Dispose();
        this._serviceScope?.Dispose();
    }
}