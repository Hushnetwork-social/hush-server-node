using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IWritableUnitOfWork<TContext> : IDisposable
    where TContext : DbContext
{
    TContext Context { get; }
    
    Task CommitAsync();
    
    Task RollbackAsync();

    TRepository GetRepository<TRepository>() 
        where TRepository : IRepository;
}
