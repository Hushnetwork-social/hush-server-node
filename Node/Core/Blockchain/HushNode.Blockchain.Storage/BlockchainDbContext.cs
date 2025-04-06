using Microsoft.EntityFrameworkCore;
using HushNode.Blockchain.Storage.Model;

namespace HushNode.Blockchain.Storage;

public class BlockchainDbContext(
    BlockchainDbContextConfigurator blockchainDbContextConfigurator,
    DbContextOptions<BlockchainDbContext> options) : DbContext(options)
{
    private readonly BlockchainDbContextConfigurator _blockchainDbContextConfigurator = blockchainDbContextConfigurator;

    public DbSet<BlockchainBlock> Blocks { get; set; }
    public DbSet<BlockchainState> BlockchainStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._blockchainDbContextConfigurator.Configure(modelBuilder);
    }
}