using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HushServerNode.CacheService;

public class BlockEntityConfigurator : IEntityTypeConfiguration<BlockEntity>
{
    public void Configure(EntityTypeBuilder<BlockEntity> builder)
    {
        builder
            .ToTable("BlockEntity")
            .HasKey(x => x.BlockId);

        builder
            .Property(x => x.BlockJson)
            .HasColumnType("jsonb")
            .IsRequired();
    }
}
