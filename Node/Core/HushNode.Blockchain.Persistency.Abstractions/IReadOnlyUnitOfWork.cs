using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.Abstractions;

public interface IReadOnlyUnitOfWork<TContext> : IDisposable
    where TContext : DbContext
{
    TContext Context { get; }

    TRepository GetRepository<TRepository>() 
        where TRepository : IRepository;
}
