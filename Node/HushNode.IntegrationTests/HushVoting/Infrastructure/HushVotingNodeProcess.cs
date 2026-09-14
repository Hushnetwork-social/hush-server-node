// FEAT-011 Phase 3 Tasks 3.3/3.4, 3.7/3.8; migration Tasks 7.M1–7.M3.
using System.Diagnostics;
using System.Text.Json;
using Grpc.Net.Client;
using HushNetwork.proto;

namespace HushVoting.IntegrationTests.Infrastructure;

/// <summary>Owns a bounded real node process; restarts preserve only its owned PG/Redis.</summary>
internal sealed class HushVotingNodeProcess(string postgres, string redis, string catalog) : IAsyncDisposable
{
    private Process? _process;
    private Task? _stderr;
    private GrpcChannel? _channel;
    public int ProcessId => _process!.Id;
    public bool HasExited => _process!.HasExited;
    public HushBlockchain.HushBlockchainClient Blockchain { get; private set; } = null!;
    public HushIdentity.HushIdentityClient Identities { get; private set; } = null!;

    public async Task StartAsync()
    {
        if (_process is not null) throw new InvalidOperationException("Test node process already owned.");
        var host = Path.Combine(AppContext.BaseDirectory, "node-process-host", "HushVoting.NodeProcessHost.dll");
        if (!File.Exists(host)) throw new InvalidOperationException("Build the owned node process host before testing.");
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(host)!, UseShellExecute = false,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        HushVotingCorpusInputs.RemoveFromChildEnvironment(start.Environment);
        start.ArgumentList.Add(host);
        try
        {
            _process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start owned test node.");
            _stderr = _process.StandardError.BaseStream.CopyToAsync(Stream.Null);
            await _process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { postgres, redis, catalog }));
            var ready = await ReceiveAsync("ready", TimeSpan.FromSeconds(30));
            if (ready.GetProperty("pid").GetInt32() != _process.Id) throw new InvalidOperationException("Wrong test node process identity.");
            var port = ready.GetProperty("port").GetInt32();
            if (port is < 1 or > 65535) throw new InvalidOperationException("Invalid test node endpoint.");
            await HushVotingArtifactClient.RegisterAsync($"localhost:{port}");
            _channel = GrpcChannel.ForAddress($"http://localhost:{port}");
            Blockchain = new(_channel);
            Identities = new(_channel);
        }
        catch { await DisposeAsync(); throw; }
    }

    public async Task ProduceBlockAsync()
    {
        await SendAsync(new { kind = "block" });
        await ReceiveAsync("block", TimeSpan.FromSeconds(15));
    }

    public async Task<(int Profiles, int Pending, int Total)> StatsAsync(string address)
    {
        await SendAsync(new { kind = "stats", address });
        var response = await ReceiveAsync("stats", TimeSpan.FromSeconds(10));
        return (response.GetProperty("profiles").GetInt32(), response.GetProperty("pending").GetInt32(), response.GetProperty("total").GetInt32());
    }

    public async Task WaitForPendingAsync(string address, int expected)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while ((await StatsAsync(address)).Pending != expected) await Task.Delay(25, timeout.Token);
    }

    public async Task<bool> HasCachedIdentityAsync(string address)
    {
        await SendAsync(new { kind = "cache", address });
        return (await ReceiveAsync("cache", TimeSpan.FromSeconds(10))).GetProperty("present").GetBoolean();
    }

    public async Task CrashAsync()
    {
        if (_process is null || _process.HasExited) throw new InvalidOperationException("No live owned node to crash.");
        _process.Kill(entireProcessTree: true);
        await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
        if (_process.ExitCode == 0) throw new InvalidOperationException("Node did not terminate abruptly.");
    }

    public async Task RestartAsync()
    {
        if (_process is null || !_process.HasExited) throw new InvalidOperationException("Crash and observe the old node before restart.");
        await DisposeAsync();
        await StartAsync();
    }

    private Task SendAsync(object command) => _process!.StandardInput.WriteLineAsync(JsonSerializer.Serialize(command));

    private async Task<JsonElement> ReceiveAsync(string kind, TimeSpan timeout)
    {
        var line = await _process!.StandardOutput.ReadLineAsync().WaitAsync(timeout);
        if (line is null || line.Length > 2048) throw new InvalidOperationException("Owned node control response unavailable.");
        using var response = JsonDocument.Parse(line);
        var actual = response.RootElement.GetProperty("kind").GetString();
        if (actual == "error") throw new InvalidOperationException("Owned node failed at "
            + response.RootElement.GetProperty("stage").GetString() + " (" + response.RootElement.GetProperty("error").GetString() + ").");
        if (actual != kind) throw new InvalidOperationException("Unexpected owned node control response.");
        return response.RootElement.Clone();
    }

    public async ValueTask DisposeAsync()
    {
        _channel?.Dispose();
        _channel = null;
        var process = _process;
        if (process is null) return;
        try
        {
            if (!process.HasExited)
            {
                process.StandardInput.Close();
                try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (TimeoutException) { process.Kill(entireProcessTree: true); }
            }
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            await (_stderr ?? Task.CompletedTask).WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            }
            process.Dispose();
            _process = null;
            _stderr = null;
        }
    }
}
