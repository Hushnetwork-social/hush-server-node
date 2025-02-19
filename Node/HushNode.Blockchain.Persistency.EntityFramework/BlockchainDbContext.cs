using Microsoft.EntityFrameworkCore;
using HushNode.Blockchain.Persistency.Abstractions.Models;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainDbContext(
    BlockchainDbContextConfigurator blockchainDbContextConfigurator,
    DbContextOptions<BlockchainDbContext> options) : DbContext(options)
{
    private readonly BlockchainDbContextConfigurator _blockchainDbContextConfigurator = blockchainDbContextConfigurator;

    // public DbSet<FinalizedBlock> Blocks { get; set; }
    public DbSet<BlockchainState> BlockchainStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._blockchainDbContextConfigurator.Configure(modelBuilder);
    }
}
