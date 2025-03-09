using Microsoft.EntityFrameworkCore;
using HushNode.Blockchain.Model;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainDbContext : DbContext
{
    private readonly BlockchainDbContextConfigurator _blockchainDbContextConfigurator;

    public DbSet<BlockchainBlock> Blocks { get; set; }
    public DbSet<BlockchainState> BlockchainStates { get; set; }

    public BlockchainDbContext(
        BlockchainDbContextConfigurator blockchainDbContextConfigurator,
        DbContextOptions<BlockchainDbContext> options) : base(options)
    {
        this._blockchainDbContextConfigurator = blockchainDbContextConfigurator;
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=HushNetworkDb;Username=HushNetworkDb_USER;Password=HushNetworkDb_PASSWORD");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        this._blockchainDbContextConfigurator.Configure(modelBuilder);
    }
}