using HushNode.Blockchain.Persistency.Abstractions;
using Microsoft.EntityFrameworkCore;

public interface IUnitOfWorkProvider<TContext> 
    where TContext : DbContext
{
    IReadOnlyUnitOfWork<TContext> CreateReadOnly();
    
    IWritableUnitOfWork<TContext> CreateWritable();
}