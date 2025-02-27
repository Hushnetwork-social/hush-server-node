using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Olimpo.EntityFramework.Persistency;

public sealed class ReadOnlyUnitOfWork<TContext> : IReadOnlyUnitOfWork<TContext> 
    where TContext : DbContext
{
    private readonly IServiceScope _serviceScope;

    public TContext Context { get; }

    public ReadOnlyUnitOfWork(IServiceProvider serviceProvider)
    {
        _serviceScope = serviceProvider.CreateScope();
        Context = _serviceScope.ServiceProvider.GetRequiredService<TContext>();

        // Configure read-only behavior
        Context.ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.NoTracking;
        Context.ChangeTracker.AutoDetectChangesEnabled = false;
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

    public void Dispose()
    {
        _serviceScope?.Dispose();
    }
}