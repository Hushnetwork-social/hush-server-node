using System.Security.Cryptography.X509Certificates;
using System.Xml.Linq;
using HushNode.Blockchain.Persistency.Abstractions.Model;
using Microsoft.EntityFrameworkCore;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainDbContext(DbContextOptions<BlockchainDbContext> options) : DbContext(options)
{
    // public DbSet<Block> Blocks { get; set; }
    public DbSet<BlockchainState> BlockchainStates { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure Block entity
        // modelBuilder
        //     .Entity<Block>()
        //     .HasKey(b => b.BlockId);

        // Configure BlockchainState (single-row table)
        modelBuilder
            .Entity<BlockchainState>()
            .HasKey(x => x.BlockchainStateId);

        modelBuilder
            .Entity<BlockchainState>()
            .Property(x => x.BlockchainStateId)
            .HasConversion(
                x => x.ToString(), 
                x => BlockchainStateIdHandler.CreateFromString(x))
            .HasColumnType("varchar(40)");

        modelBuilder
            .Entity<BlockchainState>()
            .Property(x => x.BlockIndex)
            .HasConversion(
                x => x.ToString(), 
                x => new BlockIndex(long.Parse(x)))
            .HasColumnType("varchar(20)");

        modelBuilder
            .Entity<BlockchainState>()
            .Property(x => x.CurrentBlockId)
            .HasConversion(
                x => x.ToString(), 
                x => BlockIdHandler.CreateFromString(x))
            .HasColumnType("varchar(40)");

        modelBuilder
            .Entity<BlockchainState>()
            .Property(x => x.PreviousBlockId)
            .HasConversion(
                x => x.ToString(), 
                x => BlockIdHandler.CreateFromString(x))
            .HasColumnType("varchar(40)");

        modelBuilder
            .Entity<BlockchainState>()
            .Property(x => x.NextBlockId)
            .HasConversion(
                x => x.ToString(), 
                x => BlockIdHandler.CreateFromString(x))
            .HasColumnType("varchar(40)");
    }
}
