using Microsoft.EntityFrameworkCore;
using HushNode.Blockchain.Model;

namespace HushNode.Blockchain.Repositories;

public class BlockchainDbContext : DbContext
{
    private readonly BlockchainDbContextConfigurator _blockchainDbContextConfigurator;

    public DbSet<BlockchainBlock> Blocks { get; set; }
    public DbSet<BlockchainState> BlockchainStates { get; set; }

    public BlockchainDbContext(
        BlockchainDbContextConfigurator blockchainDbContextConfigurator,
        DbContextOptions<BlockchainDbContext> options) : base(options)
    {
        _blockchainDbContextConfigurator = blockchainDbContextConfigurator;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _blockchainDbContextConfigurator.Configure(modelBuilder);
    }
}