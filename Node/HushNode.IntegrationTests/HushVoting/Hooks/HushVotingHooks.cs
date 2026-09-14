using HushVoting.IntegrationTests.Infrastructure;
using TechTalk.SpecFlow;

namespace HushVoting.IntegrationTests.Hooks;

[Binding]
[Scope(Tag = "HV-E2E")]
internal sealed class HushVotingHooks(HushVotingScenario scenario, ScenarioContext context)
{
    private static readonly SemaphoreSlim Serial = new(1, 1);
    private static HushVotingTestRun? _run;
    internal static HushVotingCorpusInputs? CorpusInputs { get; private set; }
    private bool _ownsLock;

    [BeforeScenario(Order = -100)]
    public async Task StartAsync()
    {
        if (context.ScenarioInfo.Tags.Contains("HV-EXTERNAL-QUALIFICATION"))
            throw new InvalidOperationException("External qualification is not a browser test. Use run-hushvoting-e2e.sh --qualifications to report its unmet gate; no Web execution can satisfy it.");
        await Serial.WaitAsync();
        _ownsLock = true;
        if (_run is null) CorpusInputs = HushVotingCorpusInputs.CaptureEnvironment();
        if (_run is null)
        {
            var starting = new HushVotingTestRun();
            try { await starting.StartAsync(); }
            catch
            {
                await starting.DisposeAsync();
                throw;
            }
            _run = starting;
        }
        if (context.ScenarioInfo.Tags.Contains("HV-LIC-EXPIRY"))
            scenario.HistoricalBlockClock = new HushVotingBlockClock();
        await scenario.StartAsync(_run);
    }

    [AfterScenario(Order = 100)]
    public async Task StopAsync()
    {
        try
        {
            try
            {
                if (scenario.Node is not null)
                    await HushVotingArtifactClient.RegisterAsync(scenario.BaseUrl, $"localhost:{scenario.Node.GrpcPort}");
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
        CorpusInputs?.Dispose(); CorpusInputs = null;
        if (_run is not null)
        {
            try { await _run.DisposeAsync(); }
            finally { _run = null; }
        }
    }
}
