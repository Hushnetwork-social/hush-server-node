using Microsoft.EntityFrameworkCore;
using HushNode.Blockchain.Persistency.Abstractions.Models;
using HushNode.Blockchain.Persistency.Abstractions.Models.Block;
using HushNode.Intefaces;

namespace HushNode.Blockchain.Persistency.EntityFramework;

public class BlockchainDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureBlockchainState(modelBuilder);
        ConfigureBlockchainBlock(modelBuilder);
    }

    private static void ConfigureBlockchainBlock(ModelBuilder modelBuilder)
    {
        modelBuilder
            .Entity<BlockchainBlock>()
            .HasKey(x => x.BlockId);

        modelBuilder
            .Entity<BlockchainBlock>()
            .Property(x => x.BlockId)
            .HasConversion(
                x => x.ToString(),
                x => BlockIdHandler.CreateFromString(x))
            .HasColumnType("varchar(40)");

        modelBuilder
            .Entity<BlockchainBlock>()
            .Property(x => x.BlockIndex)
            .HasConversion(
                x => x.ToString(),
                x => new BlockIndex(long.Parse(x)))
            .HasColumnType("varchar(20)");

        modelBuilder
            .Entity<BlockchainBlock>()
            .Property(x => x.BlockIndex)
            .HasConversion(
                x => x.ToString(),
                x => new BlockIndex(long.Parse(x)))
            .HasColumnType("varchar(20)");

        modelBuilder
            .Entity<BlockchainBlock>()
            .Property(x => x.PreviousBlockId)
            .HasConversion(
                x => x.ToString(),
                x => BlockIdHandler.CreateFromString(x))
            .HasColumnType("varchar(40)");

        modelBuilder
            .Entity<BlockchainBlock>()
            .Property(x => x.NextBlockId)
            .HasConversion(
                x => x.ToString(),
                x => BlockIdHandler.CreateFromString(x))
            .HasColumnType("varchar(40)");
    }

    private static void ConfigureBlockchainState(ModelBuilder modelBuilder)
    {
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
