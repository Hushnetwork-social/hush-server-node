using System.Reactive.Subjects;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushServerNode.HushVotingLicensingIntegration;

/// <summary>Runs after licence release reconciliation, before the node becomes ready.
/// Activation is unconditional: there is no enforcement-off or legacy fallback switch.</summary>
public sealed class ElectionEntitlementRolloutBootstrapper(
    ElectionEntitlementRolloutReadiness readiness, ILogger<ElectionEntitlementRolloutBootstrapper> logger) : IBootstrapper
{
    public Subject<string> BootstrapFinished { get; } = new();
    public int Priority { get; set; } = 21;
    public async Task Startup()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var result = await readiness.EvaluateAsync(deadline.Token);
        logger.LogInformation("Election entitlement rollout: {Code}; reconstructed captures: {Count}", result.Code, result.BackfilledCaptures);
        if (!result.Ready) throw new InvalidOperationException(result.Code);
        BootstrapFinished.OnNext(nameof(ElectionEntitlementRolloutBootstrapper));
    }
    public void Shutdown() { }
}
