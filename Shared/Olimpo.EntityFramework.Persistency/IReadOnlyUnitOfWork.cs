using Microsoft.EntityFrameworkCore;

namespace Olimpo.EntityFramework.Persistency;

public interface IReadOnlyUnitOfWork<TContext> : IDisposable
    where TContext : DbContext
{
    TContext Context { get; }

    TRepository GetRepository<TRepository>() 
        where TRepository : IRepository;
}
