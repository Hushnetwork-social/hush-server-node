using Microsoft.EntityFrameworkCore;
using HushNode.Interfaces;
using HushNode.Interfaces.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Configures the Entity Framework model for push notifications entities.
/// </summary>
public class PushNotificationsDbContextConfigurator : IDbContextConfigurator
{
    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureDeviceToken(modelBuilder);
    }

    private static void ConfigureDeviceToken(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<DeviceToken>(entity =>
        {
            // Table mapping
            entity.ToTable("DeviceTokens", "Notifications");

            // Primary key
            entity.HasKey(x => x.Id);

            // Id column
            entity.Property(x => x.Id)
                .HasColumnType("varchar(36)")
                .IsRequired();

            // UserId column with index for user lookup
            entity.Property(x => x.UserId)
                .HasColumnType("varchar(100)")
                .IsRequired();
            entity.HasIndex(x => x.UserId)
                .HasDatabaseName("IX_DeviceTokens_UserId");

            // Platform column (stored as int)
            entity.Property(x => x.Platform)
                .HasConversion<int>()
                .IsRequired();

            // Token column with unique constraint
            entity.Property(x => x.Token)
                .HasColumnType("varchar(512)")
                .IsRequired();
            entity.HasIndex(x => x.Token)
                .IsUnique()
                .HasDatabaseName("IX_DeviceTokens_Token");

            // DeviceName column (optional)
            entity.Property(x => x.DeviceName)
                .HasColumnType("varchar(100)")
                .IsRequired(false);

            // Timestamp columns
            entity.Property(x => x.CreatedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            entity.Property(x => x.LastUsedAt)
                .HasColumnType("timestamp with time zone")
                .IsRequired();

            // IsActive flag
            entity.Property(x => x.IsActive)
                .HasDefaultValue(true)
                .IsRequired();

            // Composite index for efficient stale token queries
            entity.HasIndex(x => new { x.IsActive, x.LastUsedAt })
                .HasDatabaseName("IX_DeviceTokens_IsActive_LastUsedAt");
        });
    }
}
