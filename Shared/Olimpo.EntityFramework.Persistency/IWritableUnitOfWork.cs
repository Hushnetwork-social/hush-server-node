using Microsoft.EntityFrameworkCore;

namespace Olimpo.EntityFramework.Persistency;

public interface IWritableUnitOfWork<TContext> : IDisposable
    where TContext : DbContext
{
    TContext Context { get; }
    
    Task CommitAsync();
    
    Task RollbackAsync();

    TRepository GetRepository<TRepository>() 
        where TRepository : IRepository;
}
