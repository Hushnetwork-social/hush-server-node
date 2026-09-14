using System.Text.Json;
using Microsoft.Playwright;

namespace HushVoting.IntegrationTests.Infrastructure;

// Test-owned access to this scenario's worker. Callers use fixed fault
// expressions; only boolean facts may leave the worker, never secret values.
internal sealed class HushVotingWorkerProbe(ICDPSession session, string workerSession) : IAsyncDisposable
{
    public static async Task<HushVotingWorkerProbe> AttachAsync(HushVotingScenario scenario)
    {
        var session = await scenario.Context.NewCDPSessionAsync(scenario.Page);
        try
        {
            var targets = await session.SendAsync("Target.getTargets");
            var worker = targets!.Value.GetProperty("targetInfos").EnumerateArray().Single(target =>
                target.GetProperty("type").GetString() == "shared_worker"
                && Uri.TryCreate(target.GetProperty("url").GetString(), UriKind.Absolute, out var url)
                && url.GetLeftPart(UriPartial.Path) == scenario.BaseUrl + "/workers/vault-shared-worker.js");
            var attached = await session.SendAsync("Target.attachToTarget", new() { ["targetId"] = worker.GetProperty("targetId").GetString()!, ["flatten"] = false });
            return new(session, attached!.Value.GetProperty("sessionId").GetString()!);
        }
        catch { await session.DetachAsync(); throw; }
    }

    public async Task<bool> EvaluateBooleanAsync(string expression)
    {
        var completed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        const int id = 1;
        EventHandler<JsonElement?> handler = (sender, payload) =>
        {
            if (payload is not { } envelope || envelope.GetProperty("sessionId").GetString() != workerSession) return;
            using var response = JsonDocument.Parse(envelope.GetProperty("message").GetString()!);
            var root = response.RootElement;
            if (!root.TryGetProperty("id", out var responseId) || responseId.GetInt32() != id) return;
            completed.TrySetResult(root.TryGetProperty("result", out var result) && !result.TryGetProperty("exceptionDetails", out _)
                && result.TryGetProperty("result", out var value) && value.TryGetProperty("value", out var boolean)
                && boolean.ValueKind == JsonValueKind.True);
        };
        session.Event("Target.receivedMessageFromTarget").OnEvent += handler;
        try
        {
            await session.SendAsync("Target.sendMessageToTarget", new()
            {
                ["sessionId"] = workerSession,
                ["message"] = JsonSerializer.Serialize(new { id, method = "Runtime.evaluate", @params = new { expression, returnByValue = true } })
            });
            return await completed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally { session.Event("Target.receivedMessageFromTarget").OnEvent -= handler; }
    }

    public async ValueTask DisposeAsync()
    {
        try { await session.SendAsync("Target.detachFromTarget", new() { ["sessionId"] = workerSession }); }
        finally { await session.DetachAsync(); }
    }
}
