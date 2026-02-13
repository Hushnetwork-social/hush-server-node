using System.Collections.Concurrent;
using Grpc.Core;
using HushNetwork.proto;

namespace HushNode.IntegrationTests.Infrastructure;

/// <summary>
/// FEAT-063: Reusable helper for managing gRPC notification stream subscriptions in integration tests.
/// Collects events in the background and provides timeout-based assertions.
/// Designed for reuse across FEAT-063, CF-005, CE-001 and future EPIC-005 features.
/// </summary>
public sealed class NotificationStreamHelper : IAsyncDisposable
{
    private readonly HushNotification.HushNotificationClient _client;
    private readonly CancellationTokenSource _cts;
    private readonly ConcurrentBag<FeedEvent> _receivedEvents = new();
    private Task? _streamTask;

    /// <summary>
    /// All events received since the stream was started.
    /// Thread-safe: can be read while the stream is still collecting.
    /// </summary>
    public IReadOnlyCollection<FeedEvent> ReceivedEvents => _receivedEvents;

    public NotificationStreamHelper(HushNotification.HushNotificationClient client)
    {
        _client = client;
        _cts = new CancellationTokenSource();
    }

    /// <summary>
    /// Starts a background task that subscribes to the notification stream and collects events.
    /// Returns immediately (non-blocking).
    /// </summary>
    public Task StartAsync(string userId, string deviceId = "test-device", string platform = "test")
    {
        var request = new SubscribeToEventsRequest
        {
            UserId = userId,
            DeviceId = deviceId,
            Platform = platform
        };

        var call = _client.SubscribeToEvents(request);

        _streamTask = Task.Run(async () =>
        {
            try
            {
                await foreach (var evt in call.ResponseStream.ReadAllAsync(_cts.Token))
                {
                    _receivedEvents.Add(evt);
                }
            }
            catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
            {
                // Expected when cancellation token is triggered during disposal
            }
            catch (OperationCanceledException)
            {
                // Expected during disposal
            }
        }, _cts.Token);

        return Task.CompletedTask;
    }

    /// <summary>
    /// Waits until an event matching the predicate is received, or timeout expires.
    /// </summary>
    /// <param name="predicate">Filter to match the desired event.</param>
    /// <param name="timeout">Maximum time to wait.</param>
    /// <returns>The first matching event.</returns>
    /// <exception cref="TimeoutException">If no matching event arrives within the timeout.</exception>
    public async Task<FeedEvent> WaitForEventAsync(
        Func<FeedEvent, bool> predicate,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            var match = _receivedEvents.FirstOrDefault(predicate);
            if (match != null)
            {
                return match;
            }

            await Task.Delay(100);
        }

        var eventTypes = string.Join(", ", _receivedEvents.Select(e => e.Type.ToString()));
        throw new TimeoutException(
            $"No matching event received within {timeout.TotalMilliseconds}ms. " +
            $"Received {_receivedEvents.Count} events: [{eventTypes}]");
    }

    /// <summary>
    /// Waits until at least the specified number of events have been received.
    /// </summary>
    public async Task WaitForEventCountAsync(int expectedCount, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;

        while (_receivedEvents.Count < expectedCount && DateTime.UtcNow < deadline)
        {
            await Task.Delay(100);
        }

        if (_receivedEvents.Count < expectedCount)
        {
            throw new TimeoutException(
                $"Expected {expectedCount} events but only received {_receivedEvents.Count} " +
                $"within {timeout.TotalMilliseconds}ms.");
        }
    }

    /// <summary>
    /// Disposes the stream cleanly: cancels the background task and closes the gRPC stream.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();

        if (_streamTask != null)
        {
            try
            {
                await _streamTask.WaitAsync(TimeSpan.FromSeconds(5));
            }
            catch (TimeoutException)
            {
                // Stream task didn't finish in time, that's ok
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
        }

        _cts.Dispose();
    }
}
