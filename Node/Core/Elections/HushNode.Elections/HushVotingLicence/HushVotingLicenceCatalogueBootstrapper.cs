using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Olimpo;
using System.Reactive.Subjects;

namespace HushNode.Elections.HushVotingLicence;

/// <summary>
/// Startup bootstrapper for the release-controlled HushVoting licence catalogue. It loads and
/// validates the catalogue once, cross-validates against the approved ceremony-profile registry,
/// and publishes one immutable snapshot. Any error fails startup readiness with safe stable codes;
/// there is no empty/previous/built-in fallback and no background/poller/watcher work.
/// </summary>
public sealed class HushVotingLicenceCatalogueBootstrapper : IBootstrapper
{
    private readonly IOptions<HushVotingLicenceOptions> _options;
    private readonly ILogger<HushVotingLicenceCatalogueBootstrapper> _logger;
    private readonly Lazy<HushVotingLicenceSnapshot> _snapshot;

    public HushVotingLicenceCatalogueBootstrapper(
        IOptions<HushVotingLicenceOptions> options,
        ILogger<HushVotingLicenceCatalogueBootstrapper> logger)
    {
        _options = options;
        _logger = logger;
        _snapshot = new Lazy<HushVotingLicenceSnapshot>(LoadSnapshot, LazyThreadSafetyMode.ExecutionAndPublication);
        BootstrapFinished = new Subject<string>();
    }

    public Subject<string> BootstrapFinished { get; }

    public int Priority { get; set; } = 15;

    public HushVotingLicenceSnapshot Snapshot => _snapshot.Value;

    public Task Startup()
    {
        _logger.LogInformation("[HushVotingLicence] Loading release-controlled licence catalogue...");

        // Touch the lazy snapshot: any validation error throws and fails startup readiness.
        _ = Snapshot;

        _logger.LogInformation(
            "[HushVotingLicence] Licence catalogue snapshot published (version {Version}).",
            Snapshot.Catalogue.Version.Value);

        BootstrapFinished.OnNext(nameof(HushVotingLicenceCatalogueBootstrapper));
        return Task.CompletedTask;
    }

    public void Shutdown()
    {
        // No background work to stop.
    }

    private HushVotingLicenceSnapshot LoadSnapshot()
    {
        var contentRoot = AppContext.BaseDirectory;
        var options = _options.Value;

        var loaded = HushVotingLicenceCatalogueHostLoader.LoadFromContentRoot(contentRoot, options);
        if (!loaded.IsValid)
        {
            foreach (var failure in loaded.Validation.Failures)
            {
                _logger.LogError(
                    "[HushVotingLicence] Catalogue validation failed. Code={Code} Path={Path} Message={Message}",
                    failure.Code,
                    failure.FieldPath,
                    failure.Message);
            }

            throw new InvalidOperationException(
                "HushVoting licence catalogue failed readiness validation. See logged LIC_CAT_* codes.");
        }

        var snapshot = new HushVotingLicenceSnapshot(loaded.Catalogue!);
        return snapshot;
    }
}
