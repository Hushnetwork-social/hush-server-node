using HushNode.Blockchain.Persistency.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public abstract class RepositoryBase<TContext> : IRepositoryWithContext<TContext>
    where TContext : DbContext
{
    protected TContext Context { get; private set; }

    public void SetContext(TContext context)
    {
        this.Context = context ?? throw new ArgumentNullException(nameof(context));
    }
}
