using System.Linq;
using System.Text.RegularExpressions;
using HushNode.IntegrationTests.Infrastructure;
using HushNode.IntegrationTests.StepDefinitions.E2E;
using HushServerNode;
using HushServerNode.Testing;
using TechTalk.SpecFlow;
using TechTalk.SpecFlow.Infrastructure;

namespace HushNode.IntegrationTests.Hooks;

/// <summary>
/// SpecFlow hooks for managing test lifecycle.
/// BeforeTestRun/AfterTestRun manage container lifecycle (once per assembly).
/// BeforeScenario/AfterScenario manage node lifecycle (once per scenario).
///
/// E2E Test Output Structure:
/// TestRun_2024-01-23_15-30-00/
///   AliceCanDecryptBobsMessage/
///     001-alice-step.png
///     server.log
///     browser-alice.log
/// </summary>
[Binding]
internal sealed class ScenarioHooks
{
    private static HushTestFixture? _fixture;
    private static readonly SemaphoreSlim _scenarioLock = new(1, 1);
    private readonly ScenarioContext _scenarioContext;

    // Test run folder (shared across all scenarios in this test run)
    private static string? _testRunFolder;
    private static readonly object _testRunFolderLock = new();

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

    /// <summary>
    /// Context key for accessing the HushTestFixture instance.
    /// </summary>
    public const string FixtureKey = "HushTestFixture";

    /// <summary>
    /// Context key for accessing the PlaywrightFixture instance.
    /// </summary>
    public const string PlaywrightKey = "PlaywrightFixture";

    /// <summary>
    /// Context key for accessing the WebClientFixture instance.
    /// </summary>
    public const string WebClientKey = "WebClientFixture";

    /// <summary>
    /// Context key for accessing the scenario output folder path.
    /// </summary>
    public const string ScenarioFolderKey = "ScenarioOutputFolder";

    /// <summary>
    /// Context key for accessing the browser console logs dictionary.
    /// </summary>
    public const string BrowserLogsKey = "BrowserConsoleLogs";

    /// <summary>
    /// Context key for accessing the executed steps list.
    /// </summary>
    public const string ExecutedStepsKey = "ExecutedSteps";

    /// <summary>
    /// Context key for the scenario start time.
    /// </summary>
    public const string ScenarioStartTimeKey = "ScenarioStartTime";

    private static PlaywrightFixture? _playwrightFixture;
    private static readonly SemaphoreSlim _playwrightLock = new(1, 1);

    private static WebClientFixture? _webClientFixture;
    private static readonly SemaphoreSlim _webClientLock = new(1, 1);

    private readonly ISpecFlowOutputHelper _outputHelper;
    private readonly FeatureContext _featureContext;

    public ScenarioHooks(ScenarioContext scenarioContext, FeatureContext featureContext, ISpecFlowOutputHelper outputHelper)
    {
        _scenarioContext = scenarioContext;
        _featureContext = featureContext;
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
    /// Stops all containers and disposes Playwright after all tests complete.
    /// </summary>
    [AfterTestRun]
    public static async Task AfterTestRun()
    {
        // Dispose WebClient container if it was started
        if (_webClientFixture != null)
        {
            await _webClientFixture.DisposeAsync();
            _webClientFixture = null;
        }

        // Dispose Playwright if it was initialized
        if (_playwrightFixture != null)
        {
            await _playwrightFixture.DisposeAsync();
            _playwrightFixture = null;
        }

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

        // Check if this is an E2E scenario (needs fixed ports for Docker)
        // Combine feature-level and scenario-level tags
        var featureTags = _featureContext.FeatureInfo.Tags ?? Array.Empty<string>();
        var scenarioTags = _scenarioContext.ScenarioInfo.Tags ?? Array.Empty<string>();
        var allTags = featureTags.Concat(scenarioTags).ToArray();
        var isE2E = allTags.Any(t => t.Equals("E2E", StringComparison.OrdinalIgnoreCase));

        _outputHelper.WriteLine($"[ScenarioHooks] FeatureTags: [{string.Join(", ", featureTags)}], ScenarioTags: [{string.Join(", ", scenarioTags)}], IsE2E: {isE2E}");

        // Start a fresh node for this scenario with diagnostic capture
        // E2E tests use fixed ports so pre-built Docker images can connect
        var (node, blockControl, grpcFactory) = isE2E
            ? await _fixture.StartNodeForE2EAsync(diagnostics)
            : await _fixture.StartNodeAsync(diagnostics);

        _outputHelper.WriteLine($"[ScenarioHooks] Node started on ports gRPC:{node.GrpcPort}, gRPC-Web:{node.GrpcWebPort}");

        // Store in ScenarioContext for step definitions
        _scenarioContext[NodeKey] = node;
        _scenarioContext[BlockControlKey] = blockControl;
        _scenarioContext[GrpcFactoryKey] = grpcFactory;
        _scenarioContext[DiagnosticsKey] = diagnostics;
        _scenarioContext[FixtureKey] = _fixture;
    }

    /// <summary>
    /// Initializes Playwright for E2E scenarios (lazy initialization on first use).
    /// Stores PlaywrightFixture in ScenarioContext for browser access.
    /// Creates scenario-specific output folder for screenshots and logs.
    /// </summary>
    [BeforeScenario("E2E")]
    [Scope(Tag = "E2E")]
    public async Task BeforeE2EScenario()
    {
        // Record scenario start time
        _scenarioContext[ScenarioStartTimeKey] = DateTime.Now;

        // Initialize executed steps list
        _scenarioContext[ExecutedStepsKey] = new List<StepExecutionInfo>();

        // Create scenario-specific folder for screenshots and logs
        var scenarioFolder = CreateScenarioFolder();
        _scenarioContext[ScenarioFolderKey] = scenarioFolder;
        Console.WriteLine($"[E2E] Scenario folder: {scenarioFolder}");

        // Initialize browser logs dictionary
        _scenarioContext[BrowserLogsKey] = new Dictionary<string, List<string>>();

        // Lazy initialization of Playwright (thread-safe)
        await _playwrightLock.WaitAsync();
        try
        {
            if (_playwrightFixture == null)
            {
                _playwrightFixture = new PlaywrightFixture();
                await _playwrightFixture.InitializeAsync();
            }

            // Configure video recording directory for this scenario
            _playwrightFixture.VideoDirectory = scenarioFolder;
            _playwrightFixture.EnableVideoRecording = true;

            // Configure trace capture (saved on failure only)
            _playwrightFixture.TraceDirectory = Path.Combine(scenarioFolder, "traces");
            _playwrightFixture.EnableTracing = true;
        }
        finally
        {
            _playwrightLock.Release();
        }

        // Store in ScenarioContext for E2E step definitions
        _scenarioContext[PlaywrightKey] = _playwrightFixture;

        // Start WebClient container with the node's gRPC port
        await StartWebClientAsync();
    }

    /// <summary>
    /// Starts the WebClient container for E2E scenarios.
    /// Uses pre-built Docker image with fixed gRPC-Web port 14666.
    /// </summary>
    private async Task StartWebClientAsync()
    {
        // Verify node is running (it should be started by BeforeScenario)
        if (!_scenarioContext.TryGetValue(NodeKey, out var nodeObj)
            || nodeObj is not HushServerNodeCore)
        {
            throw new InvalidOperationException(
                "Node must be started before WebClient. Ensure BeforeScenario runs before BeforeE2EScenario.");
        }

        await _webClientLock.WaitAsync();
        try
        {
            if (_webClientFixture == null)
            {
                _webClientFixture = new WebClientFixture();
            }

            if (!_webClientFixture.IsStarted)
            {
                // Uses pre-built image with fixed port - no rebuild needed
                await _webClientFixture.StartAsync();
            }
        }
        finally
        {
            _webClientLock.Release();
        }

        _scenarioContext[WebClientKey] = _webClientFixture;
    }

    /// <summary>
    /// Disposes the node after each scenario completes (pass or fail).
    /// Saves server and browser logs to the scenario folder for E2E tests.
    /// Outputs diagnostic logs on test failure.
    /// Releases the semaphore to allow the next scenario to run.
    /// </summary>
    [AfterScenario]
    public async Task AfterScenario()
    {
        try
        {
            // For E2E tests, close browser contexts first (ensures videos are saved)
            await CloseBrowserContextsAsync();

            // For E2E tests, save logs to scenario folder
            if (_scenarioContext.TryGetValue(ScenarioFolderKey, out var folderObj)
                && folderObj is string scenarioFolder)
            {
                SaveTestResult(scenarioFolder);
                SaveServerLogs(scenarioFolder);
                SaveBrowserLogs(scenarioFolder);
                SaveLocalStorageTimeline(scenarioFolder);
                SaveNetworkLogs(scenarioFolder);
                SaveFailureScreenshot(scenarioFolder);
                RenameVideos(scenarioFolder);
                Console.WriteLine($"[E2E] All logs saved to: {scenarioFolder}");
            }

            // Output diagnostics on test failure (also to console)
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
    /// Tracks step execution start time for E2E scenarios.
    /// </summary>
    [BeforeStep]
    [Scope(Tag = "E2E")]
    public void BeforeStep()
    {
        _scenarioContext["CurrentStepStartTime"] = DateTime.Now;
    }

    /// <summary>
    /// Records step execution result for E2E scenarios.
    /// </summary>
    [AfterStep]
    [Scope(Tag = "E2E")]
    public void AfterStep()
    {
        if (!_scenarioContext.TryGetValue(ExecutedStepsKey, out var stepsObj)
            || stepsObj is not List<StepExecutionInfo> steps)
        {
            return;
        }

        var stepInfo = _scenarioContext.StepContext.StepInfo;
        var startTime = _scenarioContext.TryGetValue("CurrentStepStartTime", out var startObj)
            ? (DateTime)startObj
            : DateTime.Now;

        var stepResult = new StepExecutionInfo
        {
            StepType = stepInfo.StepDefinitionType.ToString(),
            StepText = stepInfo.Text,
            Status = _scenarioContext.TestError == null ? "Passed" : "Failed",
            Duration = DateTime.Now - startTime,
            Error = _scenarioContext.TestError?.Message,
            StackTrace = _scenarioContext.TestError?.StackTrace
        };

        steps.Add(stepResult);
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

    /// <summary>
    /// Gets the HushTestFixture from ScenarioContext.
    /// </summary>
    public HushTestFixture GetFixture()
    {
        if (_scenarioContext.TryGetValue(FixtureKey, out var fixtureObj)
            && fixtureObj is HushTestFixture fixture)
        {
            return fixture;
        }

        throw new InvalidOperationException("HushTestFixture not found in ScenarioContext.");
    }

    /// <summary>
    /// Gets the PlaywrightFixture from ScenarioContext.
    /// Only available in scenarios tagged with @E2E.
    /// </summary>
    public PlaywrightFixture GetPlaywright()
    {
        if (_scenarioContext.TryGetValue(PlaywrightKey, out var playwrightObj)
            && playwrightObj is PlaywrightFixture playwright)
        {
            return playwright;
        }

        throw new InvalidOperationException(
            "PlaywrightFixture not found in ScenarioContext. " +
            "Ensure the scenario is tagged with @E2E.");
    }

    /// <summary>
    /// Gets the WebClientFixture from ScenarioContext.
    /// Only available in scenarios tagged with @E2E.
    /// </summary>
    public WebClientFixture GetWebClient()
    {
        if (_scenarioContext.TryGetValue(WebClientKey, out var webClientObj)
            && webClientObj is WebClientFixture webClient)
        {
            return webClient;
        }

        throw new InvalidOperationException(
            "WebClientFixture not found in ScenarioContext. " +
            "Ensure the scenario is tagged with @E2E.");
    }

    #region E2E Output Folder Management

    /// <summary>
    /// Gets or creates the test run folder (shared across all scenarios).
    /// When HUSH_TEST_OUTPUT_DIR is set (by run-tests.ps1), uses that folder.
    /// Otherwise creates: Node/TestResults/TestRun_2024-01-23_15-30-00
    /// </summary>
    private static string GetOrCreateTestRunFolder()
    {
        lock (_testRunFolderLock)
        {
            if (_testRunFolder == null)
            {
                // Check if run-tests.ps1 set the output directory
                var envDir = Environment.GetEnvironmentVariable("HUSH_TEST_OUTPUT_DIR");
                if (!string.IsNullOrEmpty(envDir))
                {
                    _testRunFolder = envDir;
                    Directory.CreateDirectory(_testRunFolder);
                    Console.WriteLine($"[E2E] Using shared test output folder: {_testRunFolder}");
                }
                else
                {
                    // Fallback: Navigate from bin/Debug to Node/TestResults
                    // Current working directory is: HushNode.IntegrationTests/bin/Debug
                    var currentDir = Directory.GetCurrentDirectory();
                    var nodeDir = Path.GetFullPath(Path.Combine(currentDir, "..", "..", "..", ".."));
                    var testResultsDir = Path.Combine(nodeDir, "TestResults");

                    var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                    _testRunFolder = Path.Combine(testResultsDir, $"TestRun_{timestamp}");
                    Directory.CreateDirectory(_testRunFolder);
                    Console.WriteLine($"[E2E] Test run folder: {_testRunFolder}");
                }
            }
            return _testRunFolder;
        }
    }

    /// <summary>
    /// Creates a scenario-specific folder for screenshots and logs.
    /// Format: TestRun_xxx/AliceCanDecryptBobsMessage/
    /// </summary>
    private string CreateScenarioFolder()
    {
        var testRunFolder = GetOrCreateTestRunFolder();
        var scenarioName = ConvertToPascalCase(_scenarioContext.ScenarioInfo.Title);
        var scenarioFolder = Path.Combine(testRunFolder, scenarioName);
        Directory.CreateDirectory(scenarioFolder);
        return scenarioFolder;
    }

    /// <summary>
    /// Converts a scenario title to PascalCase for folder naming.
    /// "Alice can decrypt Bob's message" -> "AliceCanDecryptBobsMessage"
    /// </summary>
    private static string ConvertToPascalCase(string title)
    {
        // Remove special characters and split by spaces/non-alphanumeric
        var words = Regex.Split(title, @"[^a-zA-Z0-9]+")
            .Where(w => !string.IsNullOrEmpty(w))
            .Select(w => char.ToUpper(w[0]) + w.Substring(1).ToLower());

        return string.Join("", words);
    }

    /// <summary>
    /// Saves server logs to the scenario folder.
    /// </summary>
    private void SaveServerLogs(string scenarioFolder)
    {
        if (!_scenarioContext.TryGetValue(DiagnosticsKey, out var diagnosticsObj)
            || diagnosticsObj is not DiagnosticCapture diagnostics)
        {
            return;
        }

        var logs = diagnostics.GetCapturedLogs();
        if (string.IsNullOrWhiteSpace(logs))
        {
            logs = "(No server logs captured)";
        }

        var logPath = Path.Combine(scenarioFolder, "server.log");
        File.WriteAllText(logPath, logs);
        Console.WriteLine($"[E2E] Server logs saved: server.log ({diagnostics.EntryCount} entries)");
    }

    /// <summary>
    /// Saves browser console logs to the scenario folder.
    /// Each user gets their own log file: browser-alice.log, browser-bob.log
    /// </summary>
    private void SaveBrowserLogs(string scenarioFolder)
    {
        if (!_scenarioContext.TryGetValue(BrowserLogsKey, out var logsObj)
            || logsObj is not Dictionary<string, List<string>> browserLogs)
        {
            return;
        }

        foreach (var (userName, logEntries) in browserLogs)
        {
            var logPath = Path.Combine(scenarioFolder, $"browser-{userName.ToLowerInvariant()}.log");
            var content = string.Join(Environment.NewLine, logEntries);
            if (string.IsNullOrWhiteSpace(content))
            {
                content = "(No browser console logs captured)";
            }
            File.WriteAllText(logPath, content);
            Console.WriteLine($"[E2E] Browser logs saved: browser-{userName.ToLowerInvariant()}.log ({logEntries.Count} entries)");
        }
    }

    /// <summary>
    /// Saves the test result summary to the scenario folder.
    /// Includes: scenario name, status (pass/fail), executed steps, failed step details, duration.
    /// </summary>
    private void SaveTestResult(string scenarioFolder)
    {
        var sb = new System.Text.StringBuilder();
        var scenarioInfo = _scenarioContext.ScenarioInfo;
        var testError = _scenarioContext.TestError;

        // Header
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("                           E2E TEST RESULT SUMMARY");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Test Info
        sb.AppendLine($"Scenario:    {scenarioInfo.Title}");
        sb.AppendLine($"Tags:        {string.Join(", ", scenarioInfo.Tags)}");
        sb.AppendLine($"Status:      {(testError == null ? "✓ PASSED" : "✗ FAILED")}");

        // Duration
        if (_scenarioContext.TryGetValue(ScenarioStartTimeKey, out var startObj) && startObj is DateTime startTime)
        {
            var duration = DateTime.Now - startTime;
            sb.AppendLine($"Duration:    {duration.TotalSeconds:F2}s");
        }

        sb.AppendLine($"Timestamp:   {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        // Steps Execution
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine("                              STEPS EXECUTION");
        sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
        sb.AppendLine();

        if (_scenarioContext.TryGetValue(ExecutedStepsKey, out var stepsObj)
            && stepsObj is List<StepExecutionInfo> steps)
        {
            var stepNumber = 0;
            foreach (var step in steps)
            {
                stepNumber++;
                var statusIcon = step.Status == "Passed" ? "✓" : "✗";
                var durationStr = step.Duration.TotalMilliseconds > 1000
                    ? $"{step.Duration.TotalSeconds:F2}s"
                    : $"{step.Duration.TotalMilliseconds:F0}ms";

                sb.AppendLine($"  {stepNumber,2}. [{statusIcon}] {step.StepType,-6} {step.StepText}");
                sb.AppendLine($"              Duration: {durationStr}");

                if (step.Status == "Failed" && step.Error != null)
                {
                    sb.AppendLine();
                    sb.AppendLine("      ┌─── ERROR ───────────────────────────────────────────────────────────");
                    foreach (var line in step.Error.Split('\n'))
                    {
                        sb.AppendLine($"      │ {line.TrimEnd()}");
                    }
                    sb.AppendLine("      └──────────────────────────────────────────────────────────────────────");
                }
                sb.AppendLine();
            }

            // Summary
            var passedCount = steps.Count(s => s.Status == "Passed");
            var failedCount = steps.Count(s => s.Status == "Failed");
            sb.AppendLine($"  Total Steps: {steps.Count} | Passed: {passedCount} | Failed: {failedCount}");
        }
        else
        {
            sb.AppendLine("  (No step execution data captured)");
        }

        // Error Details (if failed)
        if (testError != null)
        {
            sb.AppendLine();
            sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine("                              FAILURE DETAILS");
            sb.AppendLine("───────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine();
            sb.AppendLine($"Exception Type: {testError.GetType().Name}");
            sb.AppendLine();
            sb.AppendLine("Message:");
            sb.AppendLine($"  {testError.Message}");
            sb.AppendLine();
            sb.AppendLine("Stack Trace:");
            if (testError.StackTrace != null)
            {
                foreach (var line in testError.StackTrace.Split('\n'))
                {
                    sb.AppendLine($"  {line.TrimEnd()}");
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("                              END OF REPORT");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

        var resultPath = Path.Combine(scenarioFolder, "test-result.txt");
        File.WriteAllText(resultPath, sb.ToString());
        Console.WriteLine($"[E2E] Test result saved: test-result.txt ({(testError == null ? "PASSED" : "FAILED")})");
    }

    /// <summary>
    /// Saves the localStorage timeline showing state changes over time.
    /// </summary>
    private void SaveLocalStorageTimeline(string scenarioFolder)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("                         LOCALSTORAGE TIMELINE");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        var userNames = new[] { "Alice", "Bob", "Charlie" }; // Common test user names
        var hasAnyData = false;

        foreach (var userName in userNames)
        {
            var timelineKey = $"LocalStorageTimeline_{userName}";
            if (!_scenarioContext.TryGetValue(timelineKey, out var timelineObj)
                || timelineObj is not List<LocalStorageSnapshot> timeline
                || timeline.Count == 0)
            {
                continue;
            }

            hasAnyData = true;
            sb.AppendLine($"───────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine($"                              {userName.ToUpper()}'S LOCALSTORAGE");
            sb.AppendLine($"───────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine();

            foreach (var snapshot in timeline)
            {
                sb.AppendLine($"[{snapshot.Timestamp:HH:mm:ss.fff}] {snapshot.StepName}");
                if (snapshot.Changes.Count > 0)
                {
                    foreach (var change in snapshot.Changes)
                    {
                        sb.AppendLine(change);
                    }
                }
                else
                {
                    sb.AppendLine("  (no changes)");
                }
                sb.AppendLine();
            }

            // Final state summary
            var finalState = timeline[^1].State;
            sb.AppendLine($"  Final State ({finalState.Count} keys):");
            foreach (var (key, value) in finalState.OrderBy(kvp => kvp.Key))
            {
                var truncatedValue = value.Length > 60 ? value.Substring(0, 57) + "..." : value;
                sb.AppendLine($"    [{key}] = {truncatedValue}");
            }
            sb.AppendLine();
        }

        if (!hasAnyData)
        {
            sb.AppendLine("  (No localStorage data captured)");
            sb.AppendLine();
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

        var logPath = Path.Combine(scenarioFolder, "localstorage-timeline.txt");
        File.WriteAllText(logPath, sb.ToString());
        Console.WriteLine($"[E2E] localStorage timeline saved: localstorage-timeline.txt");
    }

    /// <summary>
    /// Saves the network/gRPC request-response log.
    /// </summary>
    private void SaveNetworkLogs(string scenarioFolder)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine("                         NETWORK/gRPC REQUEST LOG");
        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");
        sb.AppendLine();

        var userNames = new[] { "Alice", "Bob", "Charlie" }; // Common test user names
        var hasAnyData = false;

        foreach (var userName in userNames)
        {
            var networkLogKey = $"NetworkLog_{userName}";
            if (!_scenarioContext.TryGetValue(networkLogKey, out var logObj)
                || logObj is not List<NetworkLogEntry> networkLog
                || networkLog.Count == 0)
            {
                continue;
            }

            hasAnyData = true;
            sb.AppendLine($"───────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine($"                              {userName.ToUpper()}'S REQUESTS");
            sb.AppendLine($"───────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine();

            foreach (var entry in networkLog)
            {
                var icon = entry.Direction switch
                {
                    "REQUEST" => "→",
                    "RESPONSE" => entry.Status >= 200 && entry.Status < 300 ? "← ✓" : "← ✗",
                    "FAILED" => "← ✗",
                    _ => "?"
                };

                var statusInfo = entry.Status.HasValue ? $" [{entry.Status}]" : "";
                var errorInfo = !string.IsNullOrEmpty(entry.Error) ? $" ERROR: {entry.Error}" : "";

                sb.AppendLine($"[{entry.Timestamp:HH:mm:ss.fff}] {icon} {entry.Method} {entry.Url}{statusInfo}{errorInfo}");

                if (!string.IsNullOrEmpty(entry.PostData) && entry.PostData.Length > 0)
                {
                    var truncatedData = entry.PostData.Length > 200
                        ? entry.PostData.Substring(0, 197) + "..."
                        : entry.PostData;
                    sb.AppendLine($"    Body: {truncatedData}");
                }
            }

            // Summary
            var requests = networkLog.Count(e => e.Direction == "REQUEST");
            var successResponses = networkLog.Count(e => e.Direction == "RESPONSE" && e.Status >= 200 && e.Status < 300);
            var failedResponses = networkLog.Count(e => e.Direction == "RESPONSE" && e.Status >= 400);
            var errors = networkLog.Count(e => e.Direction == "FAILED");
            sb.AppendLine();
            sb.AppendLine($"  Summary: {requests} requests, {successResponses} success, {failedResponses} HTTP errors, {errors} failures");
            sb.AppendLine();
        }

        if (!hasAnyData)
        {
            sb.AppendLine("  (No network data captured)");
            sb.AppendLine();
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════════════════════════");

        var logPath = Path.Combine(scenarioFolder, "network-log.txt");
        File.WriteAllText(logPath, sb.ToString());
        Console.WriteLine($"[E2E] Network log saved: network-log.txt");
    }

    /// <summary>
    /// Saves a failure screenshot if the test failed.
    /// </summary>
    private void SaveFailureScreenshot(string scenarioFolder)
    {
        if (_scenarioContext.TestError == null)
        {
            return; // Test passed, no failure screenshot needed
        }

        // Try to get any active page to screenshot
        var userNames = new[] { "Alice", "Bob", "Charlie" };
        foreach (var userName in userNames)
        {
            var pageKey = $"E2E_Page_{userName}";
            if (_scenarioContext.TryGetValue(pageKey, out var pageObj)
                && pageObj is Microsoft.Playwright.IPage page
                && !page.IsClosed)
            {
                try
                {
                    var screenshotPath = Path.Combine(scenarioFolder, $"FAILURE-{userName.ToLowerInvariant()}.png");
                    page.ScreenshotAsync(new Microsoft.Playwright.PageScreenshotOptions
                    {
                        Path = screenshotPath,
                        FullPage = true
                    }).GetAwaiter().GetResult();
                    Console.WriteLine($"[E2E] Failure screenshot saved: FAILURE-{userName.ToLowerInvariant()}.png");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[E2E] Failed to save failure screenshot for {userName}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Closes all browser contexts to ensure videos are saved.
    /// Stops tracing before closing - saves trace on failure, discards on success.
    /// </summary>
    private async Task CloseBrowserContextsAsync()
    {
        var userNames = new[] { "Alice", "Bob", "Charlie" };
        var scenarioFailed = _scenarioContext.TestError != null;
        var scenarioName = _scenarioContext.ScenarioInfo.Title;

        foreach (var userName in userNames)
        {
            var contextKey = $"E2E_Context_{userName}";
            if (_scenarioContext.TryGetValue(contextKey, out var contextObj)
                && contextObj is Microsoft.Playwright.IBrowserContext context)
            {
                try
                {
                    // Stop tracing before closing context (save on failure only)
                    if (_playwrightFixture != null && _playwrightFixture.EnableTracing)
                    {
                        await _playwrightFixture.StopTracingAsync(
                            context,
                            scenarioFailed,
                            $"{scenarioName}-{userName}");
                    }

                    await context.CloseAsync();
                    Console.WriteLine($"[E2E] Browser context closed for {userName} (video saved)");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[E2E] Failed to close context for {userName}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Renames video files to more descriptive names.
    /// Playwright generates videos with random GUIDs, this renames them to video-alice.webm, etc.
    /// </summary>
    private void RenameVideos(string scenarioFolder)
    {
        try
        {
            var videoFiles = Directory.GetFiles(scenarioFolder, "*.webm");
            var userNames = new[] { "alice", "bob", "charlie" };
            var userIndex = 0;

            foreach (var videoFile in videoFiles.OrderBy(f => File.GetCreationTime(f)))
            {
                if (userIndex >= userNames.Length) break;

                var newName = $"video-{userNames[userIndex]}.webm";
                var newPath = Path.Combine(scenarioFolder, newName);

                if (!File.Exists(newPath))
                {
                    File.Move(videoFile, newPath);
                    Console.WriteLine($"[E2E] Video saved: {newName}");
                }

                userIndex++;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[E2E] Failed to rename videos: {ex.Message}");
        }
    }

    #endregion
}

/// <summary>
/// Information about a single step execution.
/// </summary>
internal sealed class StepExecutionInfo
{
    public required string StepType { get; init; }
    public required string StepText { get; init; }
    public required string Status { get; init; }
    public required TimeSpan Duration { get; init; }
    public string? Error { get; init; }
    public string? StackTrace { get; init; }
}
