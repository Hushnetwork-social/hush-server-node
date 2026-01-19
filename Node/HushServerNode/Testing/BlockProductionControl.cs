using System.Reactive.Linq;
using System.Reactive.Subjects;

namespace HushServerNode.Testing;

/// <summary>
/// Provides manual control over block production for integration tests.
/// This class allows tests to trigger block production synchronously and wait
/// for blocks to be finalized (persisted to the database).
/// </summary>
internal sealed class BlockProductionControl : IDisposable
{
    // Use ReplaySubject to ensure values aren't lost if emitted before subscriber subscribes.
    // This fixes the race condition where ProduceBlockAsync() is called before
    // BlockProductionSchedulerService.Handle(BlockchainInitializedEvent) subscribes.
    private readonly ReplaySubject<long> _blockTrigger = new(bufferSize: 1);
    private readonly object _lock = new();
    private TaskCompletionSource<bool>? _pendingBlockCompletion;
    private long _triggerCount;
    private bool _disposed;

    /// <summary>
    /// Gets the observable that the block production scheduler subscribes to.
    /// Each emission triggers a block production cycle.
    /// </summary>
    public IObservable<long> Observable => _blockTrigger.AsObservable();

    /// <summary>
    /// Creates a factory function that returns this control's observable.
    /// Use this when configuring the BlockProductionSchedulerService.
    /// </summary>
    public Func<IObservable<long>> CreateObservableFactory() => () => Observable;

    /// <summary>
    /// Gets the configuration tuple for BlockProductionSchedulerService.
    /// Returns the observable factory and finalization callback for wiring up the scheduler.
    /// </summary>
    /// <returns>
    /// A tuple containing:
    /// - observableFactory: The factory that provides the block trigger observable
    /// - onBlockFinalized: The callback to invoke when a block is finalized
    /// </returns>
    public (Func<IObservable<long>> observableFactory, Action onBlockFinalized) GetSchedulerConfiguration()
        => (CreateObservableFactory(), OnBlockFinalized);

    /// <summary>
    /// Triggers block production and waits for the block to be finalized.
    /// This method is synchronous from the caller's perspective - it returns
    /// only after the block has been persisted to the database.
    /// </summary>
    /// <param name="timeout">Maximum time to wait for block finalization. Defaults to 10 seconds.</param>
    /// <returns>A task that completes when the block is finalized.</returns>
    /// <exception cref="TimeoutException">Thrown if block finalization doesn't complete within the timeout.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if this instance has been disposed.</exception>
    public async Task ProduceBlockAsync(TimeSpan? timeout = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var effectiveTimeout = timeout ?? TimeSpan.FromSeconds(10);
        TaskCompletionSource<bool> tcs;

        lock (_lock)
        {
            tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingBlockCompletion = tcs;
        }

        try
        {
            // Trigger block production
            _blockTrigger.OnNext(Interlocked.Increment(ref _triggerCount));

            // Wait for finalization with timeout
            using var cts = new CancellationTokenSource(effectiveTimeout);

            try
            {
                await tcs.Task.WaitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Check if the task was cancelled due to disposal (not timeout)
                if (tcs.Task.IsCanceled)
                {
                    throw new TaskCanceledException(tcs.Task);
                }

                throw new TimeoutException(
                    $"Block production did not complete within {effectiveTimeout.TotalSeconds} seconds.");
            }
        }
        finally
        {
            lock (_lock)
            {
                if (_pendingBlockCompletion == tcs)
                {
                    _pendingBlockCompletion = null;
                }
            }
        }
    }

    /// <summary>
    /// Signals that a block has been finalized (persisted to the database).
    /// This method should be called when BlockCreatedEvent is received.
    /// </summary>
    public void OnBlockFinalized()
    {
        lock (_lock)
        {
            _pendingBlockCompletion?.TrySetResult(true);
        }
    }

    /// <summary>
    /// Triggers block production without waiting for completion.
    /// Useful for testing scenarios where immediate return is needed.
    /// </summary>
    public void TriggerBlockProduction()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _blockTrigger.OnNext(Interlocked.Increment(ref _triggerCount));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        lock (_lock)
        {
            _pendingBlockCompletion?.TrySetCanceled();
            _pendingBlockCompletion = null;
        }

        _blockTrigger.OnCompleted();
        _blockTrigger.Dispose();
    }
}
