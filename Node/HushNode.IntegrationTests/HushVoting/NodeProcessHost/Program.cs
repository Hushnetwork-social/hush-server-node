// EPIC-001 -> FEAT-011 Phase 3 Tasks 3.3/3.4 and 3.7/3.8.
// Private, bounded test host. Only lifecycle controls travel over stdin/stdout;
// identity submissions and lookups use the node's ordinary public gRPC API.
using System.Text.Json;
using HushNode.Identity.Storage;
using HushNode.MemPool;
using HushServerNode;
using HushServerNode.Testing;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using HushNode.Caching;
using HushNode.Notifications.Models;
using StackExchange.Redis;

using var output = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
Console.SetOut(TextWriter.Null);
Console.SetError(TextWriter.Null);
using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
HushServerNodeCore? node = null;
using var blocks = new BlockProductionControl();
var stage = "configuration";
try
{
    using var configuration = JsonDocument.Parse(await LineAsync());
    var settings = configuration.RootElement;
    stage = "create";
    node = HushServerNodeCore.CreateForTesting(blocks,
        settings.GetProperty("postgres").GetString()!, settings.GetProperty("redis").GetString()!,
        configurationOverrides: new Dictionary<string, string?>
        {
            ["Elections:ProtocolPackages:ApprovedCatalogRelativePath"] = settings.GetProperty("catalog").GetString(),
            ["Logging:LogLevel:Default"] = "None",
            ["Elections:DeploymentProof:LocalDevelopmentProfileIds"] = "admin-dev-1of1;admin-prod-1of1;dkg-dev-3of5;dkg-prod-3of5"
        }, resetDatabase: false);
    stage = "start";
    await node.StartAsync().WaitAsync(deadline.Token);
    await ReplyAsync(new { kind = "ready", pid = Environment.ProcessId, port = node.GrpcPort });
    while (!deadline.IsCancellationRequested)
    {
        stage = "read";
        var line = await Console.In.ReadLineAsync(deadline.Token);
        if (line is null) break; // The owning test process closed its pipe.
        if (line.Length > 8192) throw new InvalidOperationException();
        using var command = JsonDocument.Parse(line);
        stage = command.RootElement.GetProperty("kind").GetString()!;
        switch (stage)
        {
            case "block":
                await blocks.ProduceBlockAsync().WaitAsync(deadline.Token);
                await ReplyAsync(new { kind = "block" });
                break;
            case "stats":
                using (var scope = node.Services.CreateScope())
                {
                    var address = command.RootElement.GetProperty("address").GetString()!;
                    var profiles = await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Profiles
                        .CountAsync(profile => profile.PublicSigningAddress == address, deadline.Token);
                    var transactions = node.Services.GetRequiredService<IMemPoolService>().PeekPendingValidatedTransactions().ToArray();
                    var pending = transactions.OfType<SignedTransaction<FullIdentityPayload>>()
                        .Count(transaction => transaction.Payload.PublicSigningAddress == address);
                    await ReplyAsync(new { kind = "stats", profiles, pending, total = transactions.Length });
                }
                break;
            case "cache":
                {
                    var address = command.RootElement.GetProperty("address").GetString()!;
                    var prefix = node.Services.GetRequiredService<IOptions<RedisSettings>>().Value.InstanceName;
                    var database = node.Services.GetRequiredService<IConnectionMultiplexer>().GetDatabase();
                    var present = await database.KeyExistsAsync(prefix + IdentityCacheConstants.GetIdentityKey(address)).WaitAsync(deadline.Token);
                    await ReplyAsync(new { kind = "cache", present });
                }
                break;
            default: throw new InvalidOperationException();
        }
    }
    return 0;
}
catch (Exception error)
{
    // No messages, stack traces, configuration, addresses or transaction bytes.
    await ReplyAsync(new { kind = "error", stage, error = error.GetType().Name });
    return 1;
}
finally
{
    if (node is not null)
    {
        try { await node.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10)); }
        catch { /* Parent supervisor also kills/waits this owned process tree. */ }
    }
}

async Task<string> LineAsync()
{
    var line = await Console.In.ReadLineAsync(deadline.Token);
    if (line is null || line.Length > 8192) throw new InvalidOperationException();
    return line;
}
Task ReplyAsync(object value) => output.WriteLineAsync(JsonSerializer.Serialize(value));
