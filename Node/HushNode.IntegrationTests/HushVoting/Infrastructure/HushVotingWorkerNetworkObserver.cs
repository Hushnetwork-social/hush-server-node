using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Playwright;

namespace HushVoting.IntegrationTests.Infrastructure;

// FEAT-007 AC-007-066, Phase 6 Task 6.4. Passive observation of this scenario's
// SharedWorker only. Request/response bodies stay in memory and never in traces.
internal sealed class HushVotingWorkerNetworkObserver : IAsyncDisposable
{
    private readonly ICDPSession _session;
    private readonly string _worker;
    private readonly Action<string, JsonElement, JsonElement> _observe;
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _commands = new();
    private readonly ConcurrentDictionary<string, (string Path, string Body)> _requests = new();
    private readonly ConcurrentBag<Task> _reads = [];
    private int _id;
    private bool _failed;

    private HushVotingWorkerNetworkObserver(ICDPSession session, string worker, Action<string, JsonElement, JsonElement> observe)
    {
        _session = session; _worker = worker; _observe = observe;
        _session.Event("Target.receivedMessageFromTarget").OnEvent += Receive;
    }

    public static async Task<HushVotingWorkerNetworkObserver> AttachAsync(HushVotingScenario scenario,
        Action<string, JsonElement, JsonElement> observe)
    {
        var session = await scenario.Context.NewCDPSessionAsync(scenario.Page);
        HushVotingWorkerNetworkObserver? observer = null;
        var stage = "find-worker";
        try
        {
            var targets = await session.SendAsync("Target.getTargets");
            var worker = targets!.Value.GetProperty("targetInfos").EnumerateArray().Single(target =>
                target.GetProperty("type").GetString() == "shared_worker"
                && Uri.TryCreate(target.GetProperty("url").GetString(), UriKind.Absolute, out var url)
                && url.GetLeftPart(UriPartial.Path) == scenario.BaseUrl + "/workers/vault-shared-worker.js");
            stage = "attach-worker";
            var attached = await session.SendAsync("Target.attachToTarget", new() { ["targetId"] = worker.GetProperty("targetId").GetString()!, ["flatten"] = false });
            observer = new(session, attached!.Value.GetProperty("sessionId").GetString()!, observe);
            stage = "enable-network";
            await observer.CommandAsync("Network.enable", new { });
            return observer;
        }
        catch
        {
            if (observer is not null) await observer.DisposeAsync(); else await session.DetachAsync();
            throw new InvalidOperationException("Could not observe the owned worker network at " + stage + "; diagnostics withheld.");
        }
    }

    private void Receive(object? sender, JsonElement? payload)
    {
        if (payload is not { } envelope || envelope.GetProperty("sessionId").GetString() != _worker) return;
        try
        {
            using var message = JsonDocument.Parse(envelope.GetProperty("message").GetString()!);
            var root = message.RootElement;
            if (root.TryGetProperty("id", out var id))
            {
                if (_commands.TryRemove(id.GetInt32(), out var completed)) completed.TrySetResult(root.Clone());
                return;
            }
            var method = root.GetProperty("method").GetString();
            var parameters = root.GetProperty("params");
            if (method == "Network.requestWillBeSent")
            {
                var request = parameters.GetProperty("request");
                var path = new Uri(request.GetProperty("url").GetString()!).AbsolutePath;
                if (request.GetProperty("method").GetString() == "POST" && path is "/api/identity" or "/api/blockchain")
                    _requests[parameters.GetProperty("requestId").GetString()!] = (path, request.GetProperty("postData").GetString()!);
            }
            else if (method == "Network.loadingFinished" && _requests.TryRemove(parameters.GetProperty("requestId").GetString()!, out var request))
                _reads.Add(ReadAsync(parameters.GetProperty("requestId").GetString()!, request));
            else if (method == "Network.loadingFailed" && _requests.TryRemove(parameters.GetProperty("requestId").GetString()!, out _)) _failed = true;
        }
        catch { _failed = true; }
    }

    private async Task ReadAsync(string requestId, (string Path, string Body) request)
    {
        try
        {
            var result = await CommandAsync("Network.getResponseBody", new { requestId });
            if (result.GetProperty("base64Encoded").GetBoolean()) throw new InvalidOperationException();
            using var body = JsonDocument.Parse(result.GetProperty("body").GetString()!);
            using var input = JsonDocument.Parse(request.Body);
            _observe(request.Path, input.RootElement, body.RootElement);
        }
        catch { _failed = true; }
    }

    private async Task<JsonElement> CommandAsync(string method, object parameters)
    {
        var id = Interlocked.Increment(ref _id);
        var completed = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _commands[id] = completed;
        try
        {
            await _session.SendAsync("Target.sendMessageToTarget", new()
            {
                ["sessionId"] = _worker,
                ["message"] = JsonSerializer.Serialize(new { id, method, @params = parameters })
            });
            var reply = await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            if (!reply.TryGetProperty("result", out var result)) throw new InvalidOperationException("Worker observation command failed.");
            return result.Clone();
        }
        finally { _commands.TryRemove(id, out _); }
    }

    public async Task VerifyAsync()
    {
        await Task.WhenAll(_reads.ToArray());
        if (_failed) throw new InvalidOperationException("Worker network observation was incomplete; diagnostics withheld.");
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Task.WhenAll(_reads.ToArray());
            await _session.SendAsync("Target.detachFromTarget", new() { ["sessionId"] = _worker });
        }
        finally
        {
            _session.Event("Target.receivedMessageFromTarget").OnEvent -= Receive;
            _requests.Clear();
            await _session.DetachAsync();
        }
    }
}
