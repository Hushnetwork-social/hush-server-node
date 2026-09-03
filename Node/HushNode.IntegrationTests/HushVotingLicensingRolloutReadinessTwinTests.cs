using FluentAssertions;
using HushNode.Bank.Storage;
using HushNode.Blockchain.Storage;
using HushNode.Caching;
using HushNode.Elections.HushVotingLicence;
using HushNode.Elections.Storage;
using HushNode.Feeds.Storage;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.Identity.Storage;
using HushNode.Interfaces;
using HushNode.PushNotifications;
using HushNode.Reactions.Storage;
using HushServerNode.HushVotingLicensingIntegration;
using HushShared.Blockchain.BlockModel;
using HushShared.HushVoting.Licensing.Model;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Npgsql;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 Phase 6 real-PostgreSQL TwinTests for host rollout readiness: the fail-closed
/// bootstrapper reconciles the ledger and captures the watermark from the authoritative indexed
/// block height, fails startup on digest conflict (catalogue_incompatible) and unavailable height
/// (rollout_watermark_unavailable) with no rows invented, and stays idempotent on restart.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingRolloutReadinessTwinTests
{
    private const string V1 = "hushvoting-licence-catalogue/v1.0.0";
    private const string V1Schema = "hushvoting-licence-catalogue/v1";
    private static readonly string DigestA = new('A', 64);
    private static readonly string DigestB = new('B', 64);

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingRolloutReadinessTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private sealed class StubBlockchainCache(BlockIndex lastBlockIndex, bool throwOnRead = false) : IBlockchainCache
    {
        public BlockId PreviousBlockId => new(Guid.NewGuid());
        public BlockId CurrentBlockId => new(Guid.NewGuid());
        public BlockId NextBlockId => new(Guid.NewGuid());

        public BlockIndex LastBlockIndex =>
            throwOnRead ? throw new InvalidOperationException("chain not indexed") : lastBlockIndex;

        public bool BlockchainStateInDatabase => true;

        public IBlockchainCache SetBlockIndex(BlockIndex index) => this;
        public IBlockchainCache SetPreviousBlockId(BlockId id) => this;
        public IBlockchainCache SetCurrentBlockId(BlockId id) => this;
        public IBlockchainCache SetNextBlockId(BlockId id) => this;
        public IBlockchainCache IsBlockchainStateInDatabase() => this;
    }

    private ServiceProvider BuildProvider(string databaseName)
    {
        var services = new ServiceCollection();

        var builder = new NpgsqlConnectionStringBuilder(_fixture.AdminConnectionString)
        {
            Database = databaseName,
            MaxPoolSize = 24,
        };

        services.AddDbContext<HushNodeDbContext>(options => options.UseNpgsql(builder.ConnectionString));

        services.AddTransient<IDbContextConfigurator, BankDbContextConfigurator>();
        services.AddTransient<IDbContextConfigurator, BlockchainDbContextConfigurator>();
        services.AddTransient<IDbContextConfigurator, ElectionsDbContextConfigurator>();
        services.AddTransient<IDbContextConfigurator, FeedsDbContextConfigurator>();
        services.AddTransient<IDbContextConfigurator, IdentityDbContextConfigurator>();
        services.AddTransient<IDbContextConfigurator, PushNotificationsDbContextConfigurator>();
        services.AddTransient<IDbContextConfigurator, ReactionsDbContextConfigurator>();
        services.AddTransient<IDbContextConfigurator, LicensingDbContextConfigurator>();

        return services.BuildServiceProvider();
    }

    private HushVotingLicenceRolloutReadinessBootstrapper BuildBootstrapper(
        ServiceProvider provider,
        IBlockchainCache chain,
        string digest) =>
        new(
            provider,
            new HushVotingLicenceSnapshot(HushVotingLicenceCatalogueV1.CreateCatalogue()),
            new OptionsWrapper<HushVotingLicenceOptions>(new HushVotingLicenceOptions()),
            chain,
            NullLogger<HushVotingLicenceRolloutReadinessBootstrapper>.Instance,
            telemetry: null,
            specFactory: () => new LicenceReleaseInstallSpec(V1, digest, V1Schema, "hush-server-node-test", "feat013-readiness-twin"));

    private async Task<string> NewDatabaseAsync()
    {
        var databaseName = $"feat013_readiness_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        await _fixture.MigrateToHeadAsync(databaseName);
        return databaseName;
    }

    [Fact]
    public async Task Startup_reconciles_the_ledger_and_captures_the_watermark_from_the_indexed_height()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            using var provider = BuildProvider(databaseName);
            var bootstrapper = BuildBootstrapper(provider, new StubBlockchainCache(new BlockIndex(4_200_100)), DigestA);

            await bootstrapper.Startup();

            await using var verify = _fixture.CreateContext(databaseName);
            var release = await verify.Set<LicenceCatalogueReleaseEntity>().SingleAsync();
            release.CatalogueVersion.Should().Be(V1);
            release.ReleaseDigestSha256.Should().Be(DigestA);
            release.IsCurrent.Should().BeTrue();
            release.RolloutWatermarkBlockHeight.Should().Be(4_200_100);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Restart_is_idempotent_and_reuses_the_committed_watermark()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            using var provider = BuildProvider(databaseName);
            var bootstrapper = BuildBootstrapper(provider, new StubBlockchainCache(new BlockIndex(1_000)), DigestA);

            await bootstrapper.Startup();
            await bootstrapper.Startup(); // simulated restart

            await using var verify = _fixture.CreateContext(databaseName);
            var rows = await verify.Set<LicenceCatalogueReleaseEntity>().ToListAsync();
            rows.Should().ContainSingle();
            rows[0].RolloutWatermarkBlockHeight.Should().Be(1_000);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Digest_conflict_fails_startup_closed_with_catalogue_incompatible()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            using var provider = BuildProvider(databaseName);
            var first = BuildBootstrapper(provider, new StubBlockchainCache(new BlockIndex(100)), DigestA);
            await first.Startup();

            var conflicting = BuildBootstrapper(provider, new StubBlockchainCache(new BlockIndex(100)), DigestB);
            var act = async () => await conflicting.Startup();

            var exception = await act.Should().ThrowAsync<InvalidOperationException>();
            exception.WithMessage("*catalogue_incompatible*");

            await using var verify = _fixture.CreateContext(databaseName);
            var row = await verify.Set<LicenceCatalogueReleaseEntity>().SingleAsync();
            row.ReleaseDigestSha256.Should().Be(DigestA); // never overwritten
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Unavailable_block_height_fails_startup_closed_with_rollout_watermark_unavailable()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            using var provider = BuildProvider(databaseName);
            var bootstrapper = BuildBootstrapper(
                provider, new StubBlockchainCache(new BlockIndex(0), throwOnRead: true), DigestA);

            var act = async () => await bootstrapper.Startup();

            var exception = await act.Should().ThrowAsync<InvalidOperationException>();
            exception.WithMessage("*rollout_watermark_unavailable*");

            await using var verify = _fixture.CreateContext(databaseName);
            (await verify.Set<LicenceCatalogueReleaseEntity>().CountAsync()).Should().Be(0);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }
}
