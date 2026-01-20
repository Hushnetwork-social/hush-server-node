using Microsoft.EntityFrameworkCore;
using HushNode.Interfaces.Models;

namespace HushNode.PushNotifications;

/// <summary>
/// Entity Framework DbContext for push notifications data.
/// </summary>
public class PushNotificationsDbContext(
    PushNotificationsDbContextConfigurator configurator,
    DbContextOptions<PushNotificationsDbContext> options) : DbContext(options)
{
    private readonly PushNotificationsDbContextConfigurator _configurator = configurator;

    /// <summary>
    /// Device tokens for push notifications.
    /// </summary>
    public DbSet<DeviceToken> DeviceTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        _configurator.Configure(modelBuilder);
    }
}
