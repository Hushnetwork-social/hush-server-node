using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HushNode.HushVoting.Licensing.Cache;

/// <summary>
/// Bounded hosted worker that periodically drives the outbox dispatcher (claims + delivers) and the
/// delivered-row purge. The loop is cancellable, never holds a user request open, and stops
/// gracefully on shutdown. Intervals are injectable so deterministic tests never depend on the wall
/// clock or on Redis/PostgreSQL being present.
/// </summary>
public sealed class LicenceCacheOutboxWorker : IHostedService, IDisposable
{
    private readonly ILicenceCacheOutboxDispatcher _dispatcher;
    private readonly ILogger<LicenceCacheOutboxWorker> _logger;
    private readonly CancellationTokenSource _shutdown = new();
    private Task? _loop;
    private readonly Func<DateTime> _utcNow;
    private readonly Func<TimeSpan> _dispatchInterval;
    private readonly Func<TimeSpan> _purgeInterval;

    public LicenceCacheOutboxWorker(
        ILicenceCacheOutboxDispatcher dispatcher,
        ILogger<LicenceCacheOutboxWorker> logger,
        Func<DateTime>? utcNow = null,
        Func<TimeSpan>? dispatchInterval = null,
        Func<TimeSpan>? purgeInterval = null)
    {
        _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
        _dispatchInterval = dispatchInterval ?? (() => TimeSpan.FromSeconds(2));
        _purgeInterval = purgeInterval ?? (() => TimeSpan.FromHours(1));
    }

    /// <summary>Backs <see cref="IHostedService.StartAsync"/> and is public for deterministic host checks.</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_loop is not null)
        {
            return Task.CompletedTask;
        }

        _loop = Task.Run(RunLoopAsync, CancellationToken.None);
        _logger.LogInformation("Licence cache outbox worker started.");
        return Task.CompletedTask;
    }

    /// <summary>Graceful bounded shutdown (never hangs a host stop).</summary>
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _shutdown.Cancel();
        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is TimeoutException or OperationCanceledException)
            {
                _logger.LogWarning(exception, "Licence cache outbox worker stop timed out; abandoning the loop task.");
            }

            _loop = null;
        }

        _logger.LogInformation("Licence cache outbox worker stopped.");
    }

    public void Dispose()
    {
        _shutdown.Cancel();
        _shutdown.Dispose();
    }

    /// <summary>Executes one bounded dispatch pass (public for deterministic host/unit checks).</summary>
    public async Task<int> RunDispatchOnceAsync(CancellationToken cancellationToken = default)
    {
        var processed = await _dispatcher.ProcessOnceAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Licence cache outbox dispatch pass processed {Processed} rows", processed);
        return processed;
    }

    /// <summary>Executes one bounded delivered-row purge pass (public for deterministic host/unit checks).</summary>
    public async Task<int> RunPurgeOnceAsync(CancellationToken cancellationToken = default)
    {
        var purged = await _dispatcher.PurgeDeliveredOnceAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Licence cache outbox purge pass removed {Purged} delivered rows", purged);
        return purged;
    }

    private async Task RunLoopAsync()
    {
        var nextDispatchUtc = _utcNow();
        var nextPurgeUtc = _utcNow() + _purgeInterval();
        try
        {
            while (!_shutdown.IsCancellationRequested)
            {
                var now = _utcNow();
                try
                {
                    if (now >= nextDispatchUtc)
                    {
                        await RunDispatchOnceAsync(_shutdown.Token).ConfigureAwait(false);
                        nextDispatchUtc = _utcNow() + _dispatchInterval();
                    }

                    if (now >= nextPurgeUtc)
                    {
                        await RunPurgeOnceAsync(_shutdown.Token).ConfigureAwait(false);
                        nextPurgeUtc = _utcNow() + _purgeInterval();
                    }
                }
                catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception exception)
                {
                    // The outbox is a durable recovery path; a transient failure must never stop
                    // the loop or surface into any user request.
                    _logger.LogWarning(exception, "Licence cache outbox worker pass failed; retrying at the next interval.");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), _shutdown.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
    }
}
