using HushNode.Blockchain.Persistency.Abstractions.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainDbContext(DbContextOptions<BlockchainDbContext> options) : DbContext(options)
{
    public DbSet<Block> Blocks { get; set; }
    public DbSet<BlockchainState> BlockchainStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Block entity
        modelBuilder
            .Entity<Block>()
            .HasKey(b => b.BlockId);

        // Configure BlockchainState (single-row table)
        modelBuilder
            .Entity<BlockchainState>()
            .HasKey(x => x.BlockchainStateId);
    }
}
