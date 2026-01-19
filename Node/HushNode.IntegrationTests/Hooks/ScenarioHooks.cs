using HushNode.IntegrationTests.Infrastructure;
using HushServerNode;
using HushServerNode.Testing;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Infrastructure;

namespace HushNode.IntegrationTests.Hooks;

/// <summary>
/// SpecFlow hooks for managing test lifecycle.
/// BeforeTestRun/AfterTestRun manage container lifecycle (once per assembly).
/// BeforeScenario/AfterScenario manage node lifecycle (once per scenario).
/// </summary>
[Binding]
internal sealed class ScenarioHooks
{
    private static HushTestFixture? _fixture;
    private static readonly SemaphoreSlim _scenarioLock = new(1, 1);
    private readonly ScenarioContext _scenarioContext;

    /// <summary>
    /// Context key for accessing the current HushServerNodeCore instance.
    /// </summary>
    public const string NodeKey = "HushServerNode";

    /// <summary>
    /// Context key for accessing the BlockProductionControl instance.
    /// </summary>
    public const string BlockControlKey = "BlockProductionControl";

    /// <summary>
    /// Context key for accessing the GrpcClientFactory instance.
    /// </summary>
    public const string GrpcFactoryKey = "GrpcClientFactory";

    /// <summary>
    /// Context key for accessing the DiagnosticCapture instance.
    /// </summary>
    public const string DiagnosticsKey = "DiagnosticCapture";

    private readonly ISpecFlowOutputHelper _outputHelper;

    public ScenarioHooks(ScenarioContext scenarioContext, ISpecFlowOutputHelper outputHelper)
    {
        _scenarioContext = scenarioContext;
        _outputHelper = outputHelper;
    }

    /// <summary>
    /// Starts PostgreSQL and Redis containers once before any tests run.
    /// </summary>
    [BeforeTestRun]
    public static async Task BeforeTestRun()
    {
        _fixture = new HushTestFixture();
        await _fixture.InitializeAsync();
    }

    /// <summary>
    /// Stops all containers after all tests complete.
    /// </summary>
    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        if (_fixture != null)
        {
            await _fixture.DisposeAsync();
            _fixture = null;
        }
    }

    /// <summary>
    /// Resets database and Redis, then starts a fresh node for each scenario.
    /// Uses a semaphore to ensure scenarios run sequentially (xUnit may try to run them in parallel).
    /// </summary>
    [BeforeScenario]
    public async Task BeforeScenario()
    {
        // Ensure only one scenario runs at a time
        await _scenarioLock.WaitAsync();

        if (_fixture == null)
        {
            throw new InvalidOperationException("Test fixture not initialized. BeforeTestRun may not have executed.");
        }

        // Reset data stores for clean slate
        await _fixture.ResetAllAsync();

        // Create diagnostic capture for this scenario
        var diagnostics = new DiagnosticCapture();

        // Start a fresh node for this scenario with diagnostic capture
        var (node, blockControl, grpcFactory) = await _fixture.StartNodeAsync(diagnostics);

        // Store in ScenarioContext for step definitions
        _scenarioContext[NodeKey] = node;
        _scenarioContext[BlockControlKey] = blockControl;
        _scenarioContext[GrpcFactoryKey] = grpcFactory;
        _scenarioContext[DiagnosticsKey] = diagnostics;
    }

    /// <summary>
    /// Disposes the node after each scenario completes (pass or fail).
    /// Outputs diagnostic logs on test failure.
    /// Releases the semaphore to allow the next scenario to run.
    /// </summary>
    [AfterScenario]
    public async Task AfterScenario()
    {
        try
        {
            // Output diagnostics on test failure
            if (_scenarioContext.TestError != null)
            {
                OutputDiagnosticLogs();
            }

            // Dispose GrpcClientFactory
            if (_scenarioContext.TryGetValue(GrpcFactoryKey, out var grpcFactoryObj)
                && grpcFactoryObj is GrpcClientFactory grpcFactory)
            {
                grpcFactory.Dispose();
            }

            // Dispose node
            if (_scenarioContext.TryGetValue(NodeKey, out var nodeObj)
                && nodeObj is HushServerNodeCore node)
            {
                await node.DisposeAsync();
            }

            // Dispose diagnostic capture
            if (_scenarioContext.TryGetValue(DiagnosticsKey, out var diagnosticsObj)
                && diagnosticsObj is DiagnosticCapture diagnostics)
            {
                diagnostics.Dispose();
            }
        }
        finally
        {
            // Always release the semaphore to allow next scenario to run
            _scenarioLock.Release();
        }
    }

    /// <summary>
    /// Outputs captured diagnostic logs when a test fails.
    /// </summary>
    private void OutputDiagnosticLogs()
    {
        if (!_scenarioContext.TryGetValue(DiagnosticsKey, out var diagnosticsObj)
            || diagnosticsObj is not DiagnosticCapture diagnostics)
        {
            return;
        }

        var logs = diagnostics.GetCapturedLogs();
        if (string.IsNullOrWhiteSpace(logs))
        {
            _outputHelper.WriteLine("=== HushServerNode Diagnostic Logs ===");
            _outputHelper.WriteLine("(No logs captured)");
            return;
        }

        _outputHelper.WriteLine("=== HushServerNode Diagnostic Logs ===");
        _outputHelper.WriteLine($"Captured {diagnostics.EntryCount} log entries:");
        _outputHelper.WriteLine("");

        // Truncate if too large
        const int maxLength = 50000;
        if (logs.Length > maxLength)
        {
            var truncated = logs.Substring(logs.Length - maxLength);
            _outputHelper.WriteLine("... (truncated, showing last 50KB)");
            _outputHelper.WriteLine(truncated);
        }
        else
        {
            _outputHelper.WriteLine(logs);
        }

        _outputHelper.WriteLine("=== End Diagnostic Logs ===");
    }

    /// <summary>
    /// Gets the current HushServerNodeCore from ScenarioContext.
    /// </summary>
    public HushServerNodeCore GetNode()
    {
        if (_scenarioContext.TryGetValue(NodeKey, out var nodeObj)
            && nodeObj is HushServerNodeCore node)
        {
            return node;
        }

        throw new InvalidOperationException("Node not found in ScenarioContext. BeforeScenario may not have executed.");
    }

    /// <summary>
    /// Gets the BlockProductionControl from ScenarioContext.
    /// </summary>
    public BlockProductionControl GetBlockControl()
    {
        if (_scenarioContext.TryGetValue(BlockControlKey, out var controlObj)
            && controlObj is BlockProductionControl blockControl)
        {
            return blockControl;
        }

        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }

    /// <summary>
    /// Gets the GrpcClientFactory from ScenarioContext.
    /// </summary>
    public GrpcClientFactory GetGrpcFactory()
    {
        if (_scenarioContext.TryGetValue(GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }

        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }
}
