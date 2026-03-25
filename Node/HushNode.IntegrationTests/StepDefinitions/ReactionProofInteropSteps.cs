using System.Diagnostics;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HushNode.Reactions.ZK;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

[Binding]
[Scope(Feature = "Reaction proof cross-runtime interop")]
public sealed class ReactionProofInteropSteps
{
    private const string WorkspaceRootKey = "ReactionInterop.WorkspaceRoot";
    private const string TempDirectoryKey = "ReactionInterop.TempDirectory";
    private const string ProofOutputPathKey = "ReactionInterop.ProofOutputPath";
    private const string VerificationKeyPathKey = "ReactionInterop.VerificationKeyPath";
    private const string VerifierScriptPathKey = "ReactionInterop.VerifierScriptPath";
    private const string GeneratedPayloadKey = "ReactionInterop.GeneratedPayload";
    private const string GeneratedProofJsonKey = "ReactionInterop.GeneratedProofJson";
    private const string GeneratedPublicSignalsJsonKey = "ReactionInterop.GeneratedPublicSignalsJson";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ScenarioContext _scenarioContext;

    public ReactionProofInteropSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"FEAT-087 approved cross-runtime reaction proof artifacts are available")]
    public void GivenFeat087ApprovedCrossRuntimeReactionProofArtifactsAreAvailable()
    {
        var workspaceRoot = ResolveWorkspaceRoot();
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"feat087-cross-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        var proofOutputPath = Path.Combine(tempDirectory, "generated-proof.json");
        var verificationKeyPath = Path.Combine(
            workspaceRoot,
            "hush-server-node",
            "Node",
            "HushServerNode",
            "circuits",
            "omega-v1.0.0",
            "verification_key.json");
        var verifierScriptPath = Path.Combine(
            workspaceRoot,
            "hush-server-node",
            "Node",
            "Core",
            "Reactions",
            "HushNode.Reactions",
            "ZK",
            "verify-groth16.mjs");

        File.Exists(verificationKeyPath).Should().BeTrue("server verification key must exist");
        File.Exists(verifierScriptPath).Should().BeTrue("server verifier script must exist");

        _scenarioContext[WorkspaceRootKey] = workspaceRoot;
        _scenarioContext[TempDirectoryKey] = tempDirectory;
        _scenarioContext[ProofOutputPathKey] = proofOutputPath;
        _scenarioContext[VerificationKeyPathKey] = verificationKeyPath;
        _scenarioContext[VerifierScriptPathKey] = verifierScriptPath;

        Console.WriteLine($"[interop.feature] workspaceRoot={workspaceRoot}");
        Console.WriteLine($"[interop.feature] tempDirectory={tempDirectory}");
        Console.WriteLine($"[interop.feature] verificationKeyPath={verificationKeyPath}");
        Console.WriteLine($"[interop.feature] verifierScriptPath={verifierScriptPath}");
    }

    [When(@"TypeScript generates reaction proof fixture ""(.*)"" for cross-runtime interop")]
    public async Task WhenTypeScriptGeneratesReactionProofFixtureForCrossRuntimeInterop(string fixtureName)
    {
        var workspaceRoot = _scenarioContext.Get<string>(WorkspaceRootKey);
        var proofOutputPath = _scenarioContext.Get<string>(ProofOutputPathKey);
        var generatorScriptPath = Path.Combine(
            workspaceRoot,
            "hush-web-client",
            "scripts",
            "benchmark-reaction-proof.mjs");

        File.Exists(generatorScriptPath).Should().BeTrue("proof generator script must exist");

        await RunNodeCommandAsync(
            workingDirectory: Path.Combine(workspaceRoot, "hush-web-client"),
            arguments:
            [
                generatorScriptPath,
                "--fixture", fixtureName,
                "--workspace-root", Path.Combine(workspaceRoot, "hush-web-client"),
                "--output", proofOutputPath
            ],
            timeoutMs: 300000);

        var generated = JsonSerializer.Deserialize<GeneratedProofPayload>(
            await File.ReadAllTextAsync(proofOutputPath),
            JsonOptions);

        generated.Should().NotBeNull();
        generated!.Proof.Should().NotBeNull();
        generated.PublicSignals.Should().NotBeNullOrEmpty();

        _scenarioContext[GeneratedPayloadKey] = generated;
        _scenarioContext[GeneratedProofJsonKey] = JsonSerializer.Serialize(generated.Proof, JsonOptions);
        _scenarioContext[GeneratedPublicSignalsJsonKey] = JsonSerializer.Serialize(generated.PublicSignals, JsonOptions);

        Console.WriteLine($"[interop.feature] fixture={fixtureName}");
        Console.WriteLine($"[interop.feature] generatedProofPath={proofOutputPath}");
        Console.WriteLine($"[interop.feature] publicSignalsCount={generated.PublicSignals!.Length}");
    }

    [Then(@"the generated proof payload should be captured as plain JSON for .NET injection")]
    public void ThenTheGeneratedProofPayloadShouldBeCapturedAsPlainJsonForNetInjection()
    {
        var proofJson = _scenarioContext.Get<string>(GeneratedProofJsonKey);
        var publicSignalsJson = _scenarioContext.Get<string>(GeneratedPublicSignalsJsonKey);

        proofJson.Should().NotBeNullOrWhiteSpace();
        publicSignalsJson.Should().NotBeNullOrWhiteSpace();

        Console.WriteLine($"[interop.feature] proofJson={proofJson}");
        Console.WriteLine($"[interop.feature] publicSignalsJson={publicSignalsJson}");
    }

    [Then(@"the .NET reaction verifier DLL should accept the generated proof")]
    public async Task ThenTheNetReactionVerifierDllShouldAcceptTheGeneratedProof()
    {
        var generated = _scenarioContext.Get<GeneratedProofPayload>(GeneratedPayloadKey);
        var verificationKeyPath = _scenarioContext.Get<string>(VerificationKeyPathKey);
        var verifierScriptPath = _scenarioContext.Get<string>(VerifierScriptPathKey);

        var packedProof = PackProof(generated.Proof!);

        var isValid = await SnarkJsProcessGroth16Verifier.VerifyAsync(
            packedProof,
            generated.PublicSignals!,
            verificationKeyPath,
            "node",
            verifierScriptPath,
            timeoutMs: 120000,
            onLog: line => Console.WriteLine($"[interop.feature.verify] {line}"));

        Console.WriteLine($"[interop.feature] verificationResult={isValid}");
        isValid.Should().BeTrue("TypeScript-generated proof should verify through the .NET verifier DLL");
    }

    [AfterScenario]
    public void CleanupReactionProofInteropArtifacts()
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
            Console.WriteLine($"[interop.feature] cleanup failed for {tempDirectory}: {ex.Message}");
        }
    }

    private static async Task RunNodeCommandAsync(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMs)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "node",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
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
            Console.WriteLine($"[interop.feature.node.stdout] {eventArgs.Data}");
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stderr.AppendLine(eventArgs.Data);
            Console.WriteLine($"[interop.feature.node.stderr] {eventArgs.Data}");
        };

        Console.WriteLine($"[interop.feature.node] cwd={workingDirectory}");
        Console.WriteLine($"[interop.feature.node] args={string.Join(" ", arguments)}");

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
                // Best effort only.
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
        var explicitServerRoot = Environment.GetEnvironmentVariable("HUSH_SERVER_NODE_ROOT");
        if (!string.IsNullOrWhiteSpace(explicitServerRoot)
            && File.Exists(Path.Combine(explicitServerRoot, "Node", "HushServerNode.sln")))
        {
            return Path.GetFullPath(explicitServerRoot);
        }

        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Node", "HushServerNode.sln")))
            {
                return current.FullName;
            }

            if (Directory.Exists(Path.Combine(current.FullName, "hush-web-client"))
                && Directory.Exists(Path.Combine(current.FullName, "hush-server-node")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException(
            "Unable to resolve hush-server-node root from test runtime. " +
            "Set HUSH_SERVER_NODE_ROOT in CI or provide a checkout layout containing Node/HushServerNode.sln.");
    }

    private static byte[] PackProof(SnarkJsProof proof)
    {
        var bytes = new byte[256];
        var offset = 0;

        void WriteField(string value)
        {
            var fieldBytes = BigInteger.Parse(value).ToByteArray(isUnsigned: true, isBigEndian: true);
            if (fieldBytes.Length > 32)
            {
                throw new InvalidOperationException($"Proof field '{value}' does not fit in 32 bytes.");
            }

            var start = offset + (32 - fieldBytes.Length);
            Buffer.BlockCopy(fieldBytes, 0, bytes, start, fieldBytes.Length);
            offset += 32;
        }

        WriteField(proof.PiA[0]);
        WriteField(proof.PiA[1]);
        WriteField(proof.PiB[0][0]);
        WriteField(proof.PiB[0][1]);
        WriteField(proof.PiB[1][0]);
        WriteField(proof.PiB[1][1]);
        WriteField(proof.PiC[0]);
        WriteField(proof.PiC[1]);

        return bytes;
    }

    private sealed record GeneratedProofPayload(
        string? Fixture,
        string? CircuitVersion,
        string[]? PublicSignals,
        SnarkJsProof? Proof);

    private sealed record SnarkJsProof(
        [property: JsonPropertyName("pi_a")]
        string[] PiA,
        [property: JsonPropertyName("pi_b")]
        string[][] PiB,
        [property: JsonPropertyName("pi_c")]
        string[] PiC,
        string Protocol,
        string Curve);
}
