using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HushServerNode.CacheService;

public class BlockchainStateConfiguration : IEntityTypeConfiguration<BlockchainState>
{
    public void Configure(EntityTypeBuilder<BlockchainState> builder)
    {
        builder
            .ToTable("BlockchainState")
            .HasKey(x => x.BlockchainStateId);
    }
}
