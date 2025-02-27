using Microsoft.EntityFrameworkCore;

namespace Olimpo.EntityFramework.Persistency;

public abstract class RepositoryBase<TContext> : IRepositoryWithContext<TContext>
    where TContext : DbContext
{
    protected TContext Context { get; private set; }

    public void SetContext(TContext context)
    {
        Context = context ?? throw new ArgumentNullException(nameof(context));
    }
}
