using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Olimpo.EntityFramework.Persistency
{
    public interface IUnitOfWorkProvider<TContext>
        where TContext : DbContext
    {
        IReadOnlyUnitOfWork<TContext> CreateReadOnly();

        IWritableUnitOfWork<TContext> CreateWritable();

        IWritableUnitOfWork<TContext> CreateWritable(IsolationLevel isolationLevel);
    }
}