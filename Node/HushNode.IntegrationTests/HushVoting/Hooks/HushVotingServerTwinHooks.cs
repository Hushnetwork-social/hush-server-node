// EPIC-001 -> FEAT-011 Phase 3 Tasks 3.7/3.8; Phase 7 Tasks 7.M1–7.M3.
using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Hooks;

[Binding]
[Scope(Tag = "HV-SERVER-TWIN")]
internal sealed class HushVotingServerTwinHooks(HushVotingScenario scenario, ScenarioContext context, FeatureContext feature)
{
    private static readonly SemaphoreSlim Serial = new(1, 1);
    private static HushVotingTestRun? _run;
    private bool _ownsLock;

    [BeforeScenario(Order = -100)]
    public async Task StartAsync()
    {
        await Serial.WaitAsync();
        _ownsLock = true;
        if (_run is null)
        {
            var starting = new HushVotingTestRun();
            try { await starting.StartAsync(includeBrowser: false); }
            catch { await starting.DisposeAsync(); throw; }
            _run = starting;
        }
        await scenario.StartAsync(_run, includeBrowser: false,
            useNodeProcess: context.ScenarioInfo.Tags.Concat(feature.FeatureInfo.Tags)
                .Any(tag => tag is "HV-NODE-RESTART-TWIN" or "HV-NODE-RESET-TWIN" or "HV-ORIGINAL-IDENTITY-TWIN"));
    }

    [AfterScenario(Order = 100)]
    public async Task StopAsync()
    {
        try
        {
            try
            {
                if (scenario.Node is not null)
                    await HushVotingArtifactClient.RegisterAsync($"localhost:{scenario.Node.GrpcPort}");
                foreach (var transaction in scenario.Faults.SubmittedTransactions)
                    await HushVotingArtifactClient.RegisterAsync(transaction);
            }
            finally { await scenario.DisposeAsync(); }
        }
        finally { if (_ownsLock) { _ownsLock = false; Serial.Release(); } }
    }

    [AfterTestRun]
    public static async Task StopRunAsync()
    {
        if (_run is null) return;
        try { await _run.DisposeAsync(); }
        finally { _run = null; }
    }
}
