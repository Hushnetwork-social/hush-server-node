using System.Reactive.Subjects;
using HushNode.Feeds.Events;
using HushNode.Feeds.Storage;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Feeds;

public class FeedsBootstrapper :
    IBootstrapper,
    IHandle<FeedsInitializedEvent>
{
    private readonly IFeedsInitializationWorkflow _feedInitializationWorkflow;
    private readonly IAttachmentTempStorageService _attachmentTempStorageService;
    private readonly ILogger<FeedsBootstrapper> _logger;

    public Subject<string> BootstrapFinished { get; } = new Subject<string>();

    public int Priority { get; set; } = 10;

    /// <summary>
    /// FEAT-066: Default age threshold for orphan temp file cleanup (10 minutes).
    /// </summary>
    private static readonly TimeSpan OrphanCleanupMaxAge = TimeSpan.FromMinutes(10);

    public FeedsBootstrapper(
        IFeedsInitializationWorkflow feedInitializationWorkflow,
        IAttachmentTempStorageService attachmentTempStorageService,
        IEventAggregator eventAggregator,
        ILogger<FeedsBootstrapper> logger)
    {
        this._feedInitializationWorkflow = feedInitializationWorkflow;
        this._attachmentTempStorageService = attachmentTempStorageService;
        this._logger = logger;

        eventAggregator.Subscribe(this);
    }

    public void Shutdown()
    {
    }

    public async Task Startup()
    {
        // FEAT-066: Clean up orphan attachment temp files from crashed sessions
        try
        {
            await this._attachmentTempStorageService.CleanupOrphansAsync(OrphanCleanupMaxAge);
        }
        catch (Exception ex)
        {
            // Non-blocking: cleanup failure should not prevent server startup
            _logger.LogWarning(ex, "Failed to clean up orphan attachment temp files during startup");
        }

        await this._feedInitializationWorkflow.Initialize();
    }

    public void Handle(FeedsInitializedEvent message)
    {
        this.BootstrapFinished.OnNext("Feeds");
    }
}
