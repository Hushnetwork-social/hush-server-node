using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Olimpo.EntityFramework.Persistency;

public class UnitOfWorkProvider<TContext>(IServiceProvider serviceProvider) : IUnitOfWorkProvider<TContext>
    where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public IReadOnlyUnitOfWork<TContext> CreateReadOnly() =>
        new ReadOnlyUnitOfWork<TContext>(_serviceProvider);

    public IWritableUnitOfWork<TContext> CreateWritable() =>
        new WritableUnitOfWork<TContext>(_serviceProvider);

    public IWritableUnitOfWork<TContext> CreateWritable(IsolationLevel isolationLevel) =>
        new WritableUnitOfWork<TContext>(_serviceProvider, isolationLevel);
}

