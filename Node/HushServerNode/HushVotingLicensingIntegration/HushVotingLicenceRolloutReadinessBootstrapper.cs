using HushNode.Caching;
using HushNode.Elections.HushVotingLicence;
using HushNode.HushVoting.Licensing.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Olimpo;
using System.Reactive.Subjects;

namespace HushServerNode.HushVotingLicensingIntegration;

/// <summary>
/// Fail-closed startup readiness for the FEAT-013 licensing authority. Runs after the FEAT-012
/// catalogue bootstrapper (priority 20) and reconciles the append-only catalogue release ledger,
/// capturing the rollout watermark transactionally from the authoritative indexed block height.
/// Any incompatible/unsupported/watermark-unavailable state throws, failing startup with a stable
/// code; there is no fallback snapshot and no guessed watermark.
/// </summary>
public sealed class HushVotingLicenceRolloutReadinessBootstrapper : IBootstrapper
{
    private readonly IServiceProvider _services;
    private readonly HushVotingLicenceSnapshot _snapshot;
    private readonly IOptions<HushVotingLicenceOptions> _options;
    private readonly IBlockchainCache _blockchainCache;
    private readonly ILogger<HushVotingLicenceRolloutReadinessBootstrapper> _logger;
    private readonly LicenceTelemetry? _telemetry;
    private readonly Func<LicenceReleaseInstallSpec>? _specFactory;

    public HushVotingLicenceRolloutReadinessBootstrapper(
        IServiceProvider services,
        HushVotingLicenceSnapshot snapshot,
        IOptions<HushVotingLicenceOptions> options,
        IBlockchainCache blockchainCache,
        ILogger<HushVotingLicenceRolloutReadinessBootstrapper> logger,
        LicenceTelemetry? telemetry = null,
        Func<LicenceReleaseInstallSpec>? specFactory = null)
    {
        _services = services;
        _snapshot = snapshot;
        _options = options;
        _blockchainCache = blockchainCache;
        _logger = logger;
        _telemetry = telemetry;
        _specFactory = specFactory;
        BootstrapFinished = new Subject<string>();
    }

    public Subject<string> BootstrapFinished { get; }

    public int Priority { get; set; } = 20;

    public async Task Startup()
    {
        _logger.LogInformation("[HushVotingLicence] Reconcile rollout ledger and capture watermark...");

        var spec = _specFactory?.Invoke() ?? BuildInstallSpec();
        await using var db = HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(_services);

        var state = await LicenceCatalogueLedgerCoordinator.ReconcileAsync(
            db,
            spec,
            _ => Task.FromResult(_blockchainCache.LastBlockIndex.Value),
            CancellationToken.None);

        if (!state.Ready)
        {
            if (state.StableFailureCode == LicenceCatalogueLedgerCoordinator.FailureCatalogueMismatch)
            {
                _telemetry?.RecordCatalogueIncompatible();
            }

            _logger.LogError(
                "[HushVotingLicence] Rollout readiness failed closed. StableCode={StableCode} Reason={SafeReason}",
                state.StableFailureCode,
                state.FailureReason);
            throw new InvalidOperationException(
                $"[HushVotingLicence] Rollout readiness failed: {state.StableFailureCode}");
        }

        _logger.LogInformation(
            "[HushVotingLicence] Rollout ready. Outcome={Outcome} Watermark={Watermark}",
            state.Outcome,
            state.RolloutWatermarkBlockHeight);

        // FEAT-015: licence serving is index-authority only. Any legacy off-chain assignment row
        // (no originating blockchain transaction) refuses serving before the host is ready.
        var indexAuthorityEvaluator = new LicenceIndexAuthorityReadinessEvaluator(
            () => HushVotingLicensingIntegrationHostBuild.CreateFreshDbContext(_services));
        var indexAuthority = await indexAuthorityEvaluator.EvaluateAsync(CancellationToken.None);
        if (!indexAuthority.Ready)
        {
            _logger.LogError(
                "[HushVotingLicence] Index-authority readiness failed closed. StableCode={StableCode} LegacyRows={LegacyRows}",
                indexAuthority.StableCode,
                indexAuthority.LegacyAssignmentCount);
            throw new InvalidOperationException(
                $"[HushVotingLicence] Index-authority readiness failed: {indexAuthority.StableCode}");
        }

        BootstrapFinished.OnNext(nameof(HushVotingLicenceRolloutReadinessBootstrapper));
    }

    public void Shutdown()
    {
        // No background work to stop.
    }

    private LicenceReleaseInstallSpec BuildInstallSpec()
    {
        var options = _options.Value;
        var metadata = HushVotingLicenceReleaseMetadataReader.ReadFromContentRoot(
            AppContext.BaseDirectory, options);

        if (!metadata.IsValid)
        {
            throw new InvalidOperationException(
                $"[HushVotingLicence] Rollout readiness cannot build its install spec: {metadata.SafeError}");
        }

        var (serverRelease, serverHost) = HushVotingLicensingIntegrationHostBuild.ServerProvenance();

        return new LicenceReleaseInstallSpec(
            _snapshot.Catalogue.Version.Value,
            metadata.DigestSha256,
            metadata.SchemaId,
            serverRelease,
            serverHost);
    }
}
