using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using FluentAssertions;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing.Elections;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

[Binding]
[Scope(Feature = "Election crypto cross-repo interop")]
public sealed class ElectionCryptoInteropSteps
{
    private const string WebClientRootKey = "ElectionCryptoInterop.WebClientRoot";
    private const string TempDirectoryKey = "ElectionCryptoInterop.TempDirectory";
    private const string FixtureOutputPathKey = "ElectionCryptoInterop.FixtureOutputPath";
    private const string LoadedFixtureKey = "ElectionCryptoInterop.LoadedFixture";
    private const string EvaluationKey = "ElectionCryptoInterop.Evaluation";

    private readonly ScenarioContext _scenarioContext;
    private readonly ElectionCryptoFixtureLoader _fixtureLoader = new();

    public ElectionCryptoInteropSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"FEAT-107 controlled election fixture infrastructure is available")]
    public void GivenFeatControlledElectionFixtureInfrastructureIsAvailable()
    {
        var webClientRoot = ResolveWebClientRoot();
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"feat107-cross-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        _scenarioContext[WebClientRootKey] = webClientRoot;
        _scenarioContext[TempDirectoryKey] = tempDirectory;

        Console.WriteLine($"[feat107.interop] webClientRoot={webClientRoot}");
        Console.WriteLine($"[feat107.interop] tempDirectory={tempDirectory}");
    }

    [When(@"the client generates controlled fixture pack ""(.*)"" with seed ""(.*)"" choice ""(.*)"" profile ""(.*)"" tier ""(.*)"" and version ""(.*)""")]
    public async Task WhenTheClientGeneratesControlledFixturePack(
        string fixtureName,
        string seed,
        string choiceIndex,
        string profile,
        string decodeTier,
        string fixtureVersion)
    {
        var webClientRoot = _scenarioContext.Get<string>(WebClientRootKey);
        var tempDirectory = _scenarioContext.Get<string>(TempDirectoryKey);
        var generatorScriptPath = Path.Combine(webClientRoot, "scripts", "generate-election-crypto-fixture.mts");
        var outputPath = Path.Combine(tempDirectory, $"{fixtureName}.json");

        File.Exists(generatorScriptPath).Should().BeTrue("the FEAT-107 fixture generator must exist");

        await RunNodeCommandAsync(
            workingDirectory: webClientRoot,
            arguments:
            [
                "--disable-warning=MODULE_TYPELESS_PACKAGE_JSON",
                generatorScriptPath,
                "--seed", seed,
                "--choice", choiceIndex,
                "--profile", profile,
                "--tier", decodeTier,
                "--fixtureVersion", fixtureVersion,
                "--output", outputPath,
            ],
            timeoutMs: 180000);

        File.Exists(outputPath).Should().BeTrue("the FEAT-107 fixture generator should emit a JSON fixture file");
        _scenarioContext[FixtureOutputPathKey] = outputPath;

        Console.WriteLine($"[feat107.interop] fixtureName={fixtureName}");
        Console.WriteLine($"[feat107.interop] fixtureOutputPath={outputPath}");
    }

    [When(@"the server harness loads and validates that fixture pack")]
    public async Task WhenTheServerHarnessLoadsAndValidatesThatFixturePack()
    {
        var fixturePath = _scenarioContext.Get<string>(FixtureOutputPathKey);
        var fixturePack = await _fixtureLoader.LoadAsync(fixturePath);
        var versionValidation = _fixtureLoader.EvaluateVersionPolicy(fixturePack.FixtureVersion);

        var evaluation = new ElectionCryptoInteropEvaluation(
            fixturePack,
            versionValidation,
            WasSemanticValidationExecuted: false,
            PublicKeyValidation: null,
            BallotValidation: null,
            RerandomizedBallotValidation: null,
            BallotDecode: null,
            RerandomizedTallyDecode: null);

        if (versionValidation.IsAccepted)
        {
            var publicKeyValidation = ControlledElectionHarness.ValidatePublicKey(fixturePack.PublicKey);
            var ballotValidation = ControlledElectionHarness.ValidateBallot(
                fixturePack.Ballot.Ballot,
                fixturePack.Ballot.SelectionCount);
            var rerandomizedBallotValidation = ControlledElectionHarness.ValidateBallot(
                fixturePack.RerandomizedBallot.Ballot,
                fixturePack.RerandomizedBallot.SelectionCount);
            var directBallotDecode = ControlledElectionHarness.TryDecryptBallotForHarness(
                fixturePack.Ballot.Ballot,
                fixturePack.TestOnly.PrivateKey,
                fixturePack.DecodeBound);
            var tallyState = ControlledElectionHarness.AccumulateBallots(
                $"feat107-interop-{fixturePack.Profile}",
                ImmutableArray.Create(fixturePack.RerandomizedBallot.Ballot));
            var rerandomizedTallyDecode = ControlledElectionHarness.TryDecryptTallyForHarness(
                tallyState,
                fixturePack.TestOnly.PrivateKey,
                fixturePack.DecodeBound);

            evaluation = evaluation with
            {
                WasSemanticValidationExecuted = true,
                PublicKeyValidation = publicKeyValidation,
                BallotValidation = ballotValidation,
                RerandomizedBallotValidation = rerandomizedBallotValidation,
                BallotDecode = directBallotDecode,
                RerandomizedTallyDecode = rerandomizedTallyDecode,
            };
        }

        _scenarioContext[LoadedFixtureKey] = fixturePack;
        _scenarioContext[EvaluationKey] = evaluation;

        Console.WriteLine($"[feat107.interop] fixtureVersion={fixturePack.FixtureVersion}");
        Console.WriteLine($"[feat107.interop] profile={fixturePack.Profile}");
        Console.WriteLine($"[feat107.interop] decodeTier={fixturePack.DecodeTier}");
        Console.WriteLine($"[feat107.interop] versionStatus={versionValidation.Status}");
    }

    [Then(@"the fixture version policy should report ""(.*)""")]
    public void ThenTheFixtureVersionPolicyShouldReport(string expectedStatus)
    {
        var evaluation = _scenarioContext.Get<ElectionCryptoInteropEvaluation>(EvaluationKey);

        evaluation.VersionValidation.Status.Should().Be(expectedStatus);
    }

    [Then(@"the loaded fixture profile should be ""(.*)""")]
    public void ThenTheLoadedFixtureProfileShouldBe(string expectedProfile)
    {
        var fixturePack = _scenarioContext.Get<LoadedControlledElectionFixturePack>(LoadedFixtureKey);

        fixturePack.Profile.Should().Be(expectedProfile);
        fixturePack.Deterministic.Should().BeTrue("FEAT-107 cross-repo smoke should stay deterministic");
    }

    [Then(@"the loaded fixture circuit version should be ""(.*)""")]
    public void ThenTheLoadedFixtureCircuitVersionShouldBe(string expectedCircuitVersion)
    {
        var fixturePack = _scenarioContext.Get<LoadedControlledElectionFixturePack>(LoadedFixtureKey);

        fixturePack.CircuitVersion.Should().Be(expectedCircuitVersion);
    }

    [Then(@"the server should accept the loaded ballot structure")]
    public void ThenTheServerShouldAcceptTheLoadedBallotStructure()
    {
        var evaluation = _scenarioContext.Get<ElectionCryptoInteropEvaluation>(EvaluationKey);

        evaluation.WasSemanticValidationExecuted.Should().BeTrue("accepted versions should reach semantic validation");
        evaluation.PublicKeyValidation.Should().NotBeNull();
        evaluation.PublicKeyValidation!.IsValid.Should().BeTrue(evaluation.PublicKeyValidation.Notes);
        evaluation.BallotValidation.Should().NotBeNull();
        evaluation.BallotValidation!.IsValid.Should().BeTrue(evaluation.BallotValidation.Notes);
        evaluation.RerandomizedBallotValidation.Should().NotBeNull();
        evaluation.RerandomizedBallotValidation!.IsValid.Should().BeTrue(evaluation.RerandomizedBallotValidation.Notes);
    }

    [Then(@"the server should derive the same ballot meaning as the client fixture")]
    public void ThenTheServerShouldDeriveTheSameBallotMeaningAsTheClientFixture()
    {
        var evaluation = _scenarioContext.Get<ElectionCryptoInteropEvaluation>(EvaluationKey);
        var fixturePack = _scenarioContext.Get<LoadedControlledElectionFixturePack>(LoadedFixtureKey);

        evaluation.BallotDecode.Should().NotBeNull();
        evaluation.BallotDecode!.IsSuccessful.Should().BeTrue(evaluation.BallotDecode.Notes);
        evaluation.BallotDecode.DecodedCounts.Should().Equal(fixturePack.Ballot.ExpectedPlaintextSlots);
    }

    [Then(@"the server should derive the expected tally meaning from the rerandomized ballot")]
    public void ThenTheServerShouldDeriveTheExpectedTallyMeaningFromTheRerandomizedBallot()
    {
        var evaluation = _scenarioContext.Get<ElectionCryptoInteropEvaluation>(EvaluationKey);
        var fixturePack = _scenarioContext.Get<LoadedControlledElectionFixturePack>(LoadedFixtureKey);

        evaluation.RerandomizedTallyDecode.Should().NotBeNull();
        evaluation.RerandomizedTallyDecode!.IsSuccessful.Should().BeTrue(evaluation.RerandomizedTallyDecode.Notes);
        evaluation.RerandomizedTallyDecode.DecodedCounts.Should().Equal(fixturePack.ExpectedAggregateTally);
    }

    [Then(@"the server should refuse further ballot interpretation")]
    public void ThenTheServerShouldRefuseFurtherBallotInterpretation()
    {
        var evaluation = _scenarioContext.Get<ElectionCryptoInteropEvaluation>(EvaluationKey);

        evaluation.VersionValidation.IsAccepted.Should().BeFalse("vulnerable or unknown versions must be rejected");
        evaluation.WasSemanticValidationExecuted.Should().BeFalse("rejected fixture versions must stop before ballot interpretation");
        evaluation.PublicKeyValidation.Should().BeNull();
        evaluation.BallotValidation.Should().BeNull();
        evaluation.BallotDecode.Should().BeNull();
        evaluation.RerandomizedTallyDecode.Should().BeNull();
    }

    [AfterScenario]
    public void CleanupElectionCryptoInteropArtifacts()
    {
        if (!_scenarioContext.TryGetValue(TempDirectoryKey, out string? tempDirectory)
            || string.IsNullOrWhiteSpace(tempDirectory)
            || !Directory.Exists(tempDirectory))
        {
            return;
        }

        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[feat107.interop] cleanup failed for {tempDirectory}: {ex.Message}");
        }
    }

    private static async Task RunNodeCommandAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        int timeoutMs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stdout.AppendLine(eventArgs.Data);
            Console.WriteLine($"[feat107.interop.node.stdout] {eventArgs.Data}");
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stderr.AppendLine(eventArgs.Data);
            Console.WriteLine($"[feat107.interop.node.stderr] {eventArgs.Data}");
        };

        Console.WriteLine($"[feat107.interop.node] cwd={workingDirectory}");
        Console.WriteLine($"[feat107.interop.node] args={string.Join(" ", arguments)}");

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeoutMs);

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
                // Best effort cleanup only.
            }

            throw new TimeoutException(
                $"Node command timed out after {timeoutMs}ms.{Environment.NewLine}" +
                $"WorkingDirectory: {workingDirectory}{Environment.NewLine}" +
                $"Arguments: {string.Join(" ", arguments)}{Environment.NewLine}" +
                $"Captured stdout:{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"Captured stderr:{Environment.NewLine}{stderr}");
        }

        process.ExitCode.Should().Be(
            0,
            $"node command should succeed.{Environment.NewLine}stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
    }

    private static string ResolveWorkspaceRoot()
    {
        throw new NotSupportedException("Use ResolveWebClientRoot for FEAT-107 interop paths.");
    }

    private static string ResolveWebClientRoot()
    {
        var attemptedPaths = new List<string>();

        var explicitWebClientRoot = Environment.GetEnvironmentVariable("HUSH_WEB_CLIENT_ROOT");
        if (TryResolveWebClientRoot(explicitWebClientRoot, attemptedPaths, out var resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        var serverRoot = ResolveServerRepositoryRoot();
        var nestedWebClientRoot = Path.Combine(serverRoot, "hush-web-client");
        if (TryResolveWebClientRoot(nestedWebClientRoot, attemptedPaths, out resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        var siblingWebClientRoot = Path.Combine(
            Directory.GetParent(serverRoot)?.FullName ?? serverRoot,
            "hush-web-client");
        if (TryResolveWebClientRoot(siblingWebClientRoot, attemptedPaths, out resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var monorepoCandidate = Path.Combine(current.FullName, "hush-web-client");
            if (TryResolveWebClientRoot(monorepoCandidate, attemptedPaths, out resolvedWebClientRoot))
            {
                return resolvedWebClientRoot;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Unable to resolve hush-web-client root from test runtime. " +
            "Set HUSH_WEB_CLIENT_ROOT in CI or provide a checkout layout containing the client repository. " +
            $"Attempted: {string.Join(", ", attemptedPaths.Distinct())}");
    }

    private static string ResolveServerRepositoryRoot()
    {
        var attemptedPaths = new List<string>();

        var explicitServerRoot = Environment.GetEnvironmentVariable("HUSH_SERVER_NODE_ROOT");
        if (TryResolveServerRepositoryRoot(explicitServerRoot, attemptedPaths, out var resolvedServerRoot))
        {
            return resolvedServerRoot;
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (TryResolveServerRepositoryRoot(current.FullName, attemptedPaths, out resolvedServerRoot))
            {
                return resolvedServerRoot;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Unable to resolve hush-server-node root from test runtime. " +
            "Set HUSH_SERVER_NODE_ROOT in CI or run from a repository checkout containing Node/HushServerNode.sln. " +
            $"Attempted: {string.Join(", ", attemptedPaths.Distinct())}");
    }

    private static bool TryResolveWebClientRoot(
        string? candidate,
        List<string> attemptedPaths,
        out string resolvedRoot)
    {
        resolvedRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(candidate);
        attemptedPaths.Add(fullPath);

        if (!Directory.Exists(fullPath))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(fullPath, "package.json")))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(fullPath, "scripts", "generate-election-crypto-fixture.mts")))
        {
            return false;
        }

        resolvedRoot = fullPath;
        return true;
    }

    private static bool TryResolveServerRepositoryRoot(
        string? candidate,
        List<string> attemptedPaths,
        out string resolvedRoot)
    {
        resolvedRoot = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var fullPath = Path.GetFullPath(candidate);
        attemptedPaths.Add(fullPath);

        if (!Directory.Exists(fullPath))
        {
            return false;
        }

        if (!File.Exists(Path.Combine(fullPath, "Node", "HushServerNode.sln")))
        {
            return false;
        }

        resolvedRoot = fullPath;
        return true;
    }

    private sealed record ElectionCryptoInteropEvaluation(
        LoadedControlledElectionFixturePack FixturePack,
        ElectionCryptoFixtureVersionValidation VersionValidation,
        bool WasSemanticValidationExecuted,
        ControlledElectionValidationResult? PublicKeyValidation,
        ControlledElectionValidationResult? BallotValidation,
        ControlledElectionValidationResult? RerandomizedBallotValidation,
        ControlledElectionDecodeResult? BallotDecode,
        ControlledElectionDecodeResult? RerandomizedTallyDecode);
}
