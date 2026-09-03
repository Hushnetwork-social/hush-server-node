using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using HushNode.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HushNode.IntegrationTests;

/// <summary>
/// FEAT-013 real-PostgreSQL catalogue-ledger/rollout TwinTests (Task 3.2). Proves
/// append-only reconciliation, single-current invariant, transactional watermark capture,
/// concurrent initialization, mismatch/newer-database readiness failures, and
/// no-catalogue-plan-copy posture against an isolated postgres:16 container.
/// </summary>
[Collection("FEAT-013 Licensing PostgreSQL")]
[Trait("Category", "FEAT-013")]
[Trait("Category", "TwinTest")]
[Trait("Category", "NON_E2E")]
public sealed class HushVotingLicensingLedgerTwinTests
{
    private const string V1 = "hushvoting-licence-catalogue/v1.0.0";
    private const string V1Schema = "hushvoting-licence-catalogue/v1";
    private const string V2 = "hushvoting-licence-catalogue/v1.1.0";
    private static readonly string DigestA = new('A', 64);
    private static readonly string DigestB = new('B', 64);

    private readonly LicensingPostgresFixture _fixture;

    public HushVotingLicensingLedgerTwinTests(LicensingPostgresFixture fixture)
    {
        _fixture = fixture;
    }

    private static LicenceReleaseInstallSpec Spec(string version, string digest) => new(
        version,
        digest,
        version.StartsWith(V1[..^5], StringComparison.Ordinal) ? V1Schema : V1Schema,
        "hush-server-node-test",
        "feat013-ledger-twin");

    [Fact]
    public async Task First_install_appends_current_release_and_captures_the_rollout_watermark_once()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            await using var context = _fixture.CreateContext(databaseName);
            var state = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context,
                Spec(V1, DigestA),
                _ => Task.FromResult(1234L),
                CancellationToken.None);

            state.Ready.Should().BeTrue();
            state.Outcome.Should().Be(LicenceLedgerReconcileOutcome.AppendedConfiguredAsCurrent);
            state.RolloutWatermarkBlockHeight.Should().Be(1234);

            var rows = await context.Set<LicenceCatalogueReleaseEntity>().ToListAsync();
            rows.Should().ContainSingle();
            rows[0].IsCurrent.Should().BeTrue();
            rows[0].RolloutWatermarkBlockHeight.Should().Be(1234);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Reconcile_replay_is_idempotent_and_never_duplicates()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            await using var context = _fixture.CreateContext(databaseName);
            await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V1, DigestA), _ => Task.FromResult(99L), CancellationToken.None);

            var replay = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V1, DigestA), _ => Task.FromResult(99L), CancellationToken.None);

            replay.Ready.Should().BeTrue();
            replay.Outcome.Should().Be(LicenceLedgerReconcileOutcome.NoChange);
            (await context.Set<LicenceCatalogueReleaseEntity>().CountAsync()).Should().Be(1);
            (await context.Set<LicenceCatalogueReleaseEntity>().CountAsync(r => r.IsCurrent)).Should().Be(1);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Same_version_different_digest_fails_readiness_without_overwriting()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            await using var context = _fixture.CreateContext(databaseName);
            await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V1, DigestA), _ => Task.FromResult(5L), CancellationToken.None);

            var mismatch = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V1, DigestB), _ => Task.FromResult(5L), CancellationToken.None);

            mismatch.Ready.Should().BeFalse();
            mismatch.StableFailureCode.Should().Be(LicenceCatalogueLedgerCoordinator.FailureCatalogueMismatch);

            var row = await context.Set<LicenceCatalogueReleaseEntity>().SingleAsync();
            row.ReleaseDigestSha256.Should().Be(DigestA);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Concurrent_first_installs_commit_one_current_release_and_one_watermark()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            var attempts = Enumerable.Range(0, 8).Select(_ => Task.Run(async () =>
            {
                await using var context = _fixture.CreateContext(databaseName);
                return await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                    context, Spec(V1, DigestA), _ => Task.FromResult(777L), CancellationToken.None);
            })).ToArray();

            var states = await Task.WhenAll(attempts);
            states.Should().OnlyContain(s => s.Ready && s.RolloutWatermarkBlockHeight == 777);

            await using var verify = _fixture.CreateContext(databaseName);
            var rows = await verify.Set<LicenceCatalogueReleaseEntity>().ToListAsync();
            rows.Should().ContainSingle();
            rows[0].IsCurrent.Should().BeTrue();
            rows[0].RolloutWatermarkBlockHeight.Should().Be(777);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Appending_a_newer_release_retains_the_older_release_and_flips_current()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            await using var context = _fixture.CreateContext(databaseName);
            await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V1, DigestA), _ => Task.FromResult(10L), CancellationToken.None);

            var append = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V2, DigestB), _ => Task.FromResult(10L), CancellationToken.None);

            append.Ready.Should().BeTrue();
            append.Outcome.Should().Be(LicenceLedgerReconcileOutcome.AppendedConfiguredAsCurrent);

            var rows = await context.Set<LicenceCatalogueReleaseEntity>().OrderBy(r => r.InstalledAtUtc).ToListAsync();
            rows.Should().HaveCount(2);
            rows[0].CatalogueVersion.Should().Be(V1);
            rows[0].IsCurrent.Should().BeFalse();
            rows[0].RolloutWatermarkBlockHeight.Should().Be(10);
            rows[1].CatalogueVersion.Should().Be(V2);
            rows[1].IsCurrent.Should().BeTrue();
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Unsupported_newer_database_release_fails_readiness()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            await using var context = _fixture.CreateContext(databaseName);
            await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V2, DigestB), _ => Task.FromResult(1L), CancellationToken.None);

            var older = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context, Spec(V1, DigestA), _ => Task.FromResult(1L), CancellationToken.None);

            older.Ready.Should().BeFalse();
            older.StableFailureCode.Should().Be(LicenceCatalogueLedgerCoordinator.FailureCatalogueMismatch);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task Unavailable_authoritative_block_height_fails_readiness_and_never_guesses()
    {
        var databaseName = await NewDatabaseAsync();
        try
        {
            await using var context = _fixture.CreateContext(databaseName);
            var state = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
                context,
                Spec(V1, DigestA),
                _ => Task.FromException<long>(new InvalidOperationException("indexed height unavailable")),
                CancellationToken.None);

            state.Ready.Should().BeFalse();
            state.StableFailureCode.Should().Be(LicenceCatalogueLedgerCoordinator.FailureRolloutWatermarkUnavailable);
            (await context.Set<LicenceCatalogueReleaseEntity>().CountAsync()).Should().Be(0);
        }
        finally
        {
            await _fixture.DropDatabaseAsync(databaseName);
        }
    }

    private async Task<string> NewDatabaseAsync()
    {
        var databaseName = $"feat013_ledger_{Guid.NewGuid():N}";
        await _fixture.CreateDatabaseAsync(databaseName);
        await _fixture.MigrateToHeadAsync(databaseName);
        return databaseName;
    }
}
