using HushNode.Blockchain.Persistency.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class UnitOfWorkProvider<TContext>(IServiceProvider serviceProvider) : IUnitOfWorkProvider<TContext>
    where TContext : DbContext
{
    private readonly IServiceProvider _serviceProvider = serviceProvider;

    public IReadOnlyUnitOfWork<TContext> CreateReadOnly() =>
        new ReadOnlyUnitOfWork<TContext>(_serviceProvider);

    public IWritableUnitOfWork<TContext> CreateWritable() =>
        new WritableUnitOfWork<TContext>(_serviceProvider);
}

