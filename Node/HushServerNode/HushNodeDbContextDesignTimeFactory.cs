using HushNode.Bank.Storage;
using HushNode.Blockchain.Storage;
using HushNode.Elections.Storage;
using HushNode.Feeds.Storage;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Identity.Storage;
using HushNode.Interfaces;
using HushNode.PushNotifications;
using HushNode.Reactions.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace HushServerNode;

/// <summary>
/// Design-time factory for <see cref="HushNodeDbContext"/> so EF Core tooling can
/// scaffold migrations without building (and migrating) the full application host.
/// The configurator list mirrors the module registrations that feed the unified model.
/// </summary>
public sealed class HushNodeDbContextDesignTimeFactory : IDesignTimeDbContextFactory<HushNodeDbContext>
{
    public HushNodeDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<HushNodeDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time_only;Username=postgres;Password=postgres")
            .Options;

        IDbContextConfigurator[] configurators =
        {
            new BankDbContextConfigurator(),
            new BlockchainDbContextConfigurator(),
            new ElectionsDbContextConfigurator(),
            new FeedsDbContextConfigurator(),
            new IdentityDbContextConfigurator(),
            new PushNotificationsDbContextConfigurator(),
            new ReactionsDbContextConfigurator(),
            new LicensingDbContextConfigurator()
        };

        return new HushNodeDbContext(configurators, options);
    }
}
