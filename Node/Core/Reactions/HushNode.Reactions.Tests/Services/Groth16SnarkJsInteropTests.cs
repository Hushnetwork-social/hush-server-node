using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HushNode.Reactions.ZK;
using Xunit;
using Xunit.Abstractions;

namespace HushNode.Reactions.Tests.Services;

public sealed class Groth16SnarkJsInteropTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITestOutputHelper _output;

    public Groth16SnarkJsInteropTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private void LogProgress(string line)
    {
        _output.WriteLine(line);
        Console.Error.WriteLine(line);
    }

    [Fact]
    [Trait("Category", "HS-INT-087-CROSS-RUNTIME-PROOF")]
    public async Task RealCircuitProof_GeneratedFromApprovedArtifacts_VerifiesThroughServerSnarkJsPath()
    {
        LogProgress("[interop] Starting real circuit proof interop test.");
        var serverRepositoryRoot = ResolveServerRepositoryRoot();
        var webClientRoot = ResolveWebClientRoot(serverRepositoryRoot);
        var tempDirectory = Path.Combine(Path.GetTempPath(), $"feat087-interop-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var proofOutputPath = Path.Combine(tempDirectory, "generated-proof.json");
            var generatorScriptPath = Path.Combine(
                webClientRoot,
                "scripts",
                "benchmark-reaction-proof.mjs");
            var verificationKeyPath = Path.Combine(
                serverRepositoryRoot,
                "Node",
                "HushServerNode",
                "circuits",
                "omega-v1.0.0",
                "verification_key.json");
            var verifierScriptPath = Path.Combine(
                serverRepositoryRoot,
                "Node",
                "Core",
                "Reactions",
                "HushNode.Reactions",
                "ZK",
                "verify-groth16.mjs");

            File.Exists(generatorScriptPath).Should().BeTrue("proof generator script must exist");
            File.Exists(verificationKeyPath).Should().BeTrue("server verification key must exist");
            File.Exists(verifierScriptPath).Should().BeTrue("server verifier script must exist");

            LogProgress($"[interop] Server repository root: {serverRepositoryRoot}");
            LogProgress($"[interop] Web client root: {webClientRoot}");
            LogProgress($"[interop] Generator script: {generatorScriptPath}");
            LogProgress($"[interop] Verification key: {verificationKeyPath}");
            LogProgress($"[interop] Verifier script: {verifierScriptPath}");
            LogProgress("[interop] Generating real proof via benchmark-reaction-proof.mjs");

            await RunNodeCommandAsync(
                workingDirectory: webClientRoot,
                arguments:
                [
                    generatorScriptPath,
                    "--fixture", "first",
                    "--workspace-root", webClientRoot,
                    "--output", proofOutputPath
                ],
                timeoutMs: 300000);

            LogProgress($"[interop] Reading generated proof from: {proofOutputPath}");

            var generated = JsonSerializer.Deserialize<GeneratedProofPayload>(
                await File.ReadAllTextAsync(proofOutputPath),
                JsonOptions);

            generated.Should().NotBeNull();
            generated!.PublicSignals.Should().NotBeNullOrEmpty();
            generated.Proof.Should().NotBeNull();

            var packedProof = PackProof(generated.Proof!);

            LogProgress($"[interop] Generated publicSignals count: {generated.PublicSignals!.Length}");
            LogProgress("[interop] Verifying proof through server snarkjs path");

            var isValid = await SnarkJsProcessGroth16Verifier.VerifyAsync(
                packedProof,
                generated.PublicSignals!,
                verificationKeyPath,
                "node",
                verifierScriptPath,
                timeoutMs: 120000,
                onLog: line => LogProgress($"[verify] {line}"));

            LogProgress($"[interop] Verification result: {isValid}");

            isValid.Should().BeTrue(
                "a real proof generated from approved omega-v1.0.0 artifacts should verify via the server snarkjs path");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private async Task RunNodeCommandAsync(string workingDirectory, IReadOnlyList<string> arguments, int timeoutMs)
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
        var startedAt = Stopwatch.StartNew();
        LogProgress($"[node] cwd={workingDirectory}");
        LogProgress($"[node] args={string.Join(" ", arguments)}");

        process.OutputDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stdout.AppendLine(eventArgs.Data);
            LogProgress($"[node stdout] {eventArgs.Data}");
        };

        process.ErrorDataReceived += (_, eventArgs) =>
        {
            if (eventArgs.Data is null)
            {
                return;
            }

            stderr.AppendLine(eventArgs.Data);
            LogProgress($"[node stderr] {eventArgs.Data}");
        };

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
                // Best effort only; preserve original timeout failure below.
            }

            throw new TimeoutException(
                $"Node command timed out after {timeoutMs}ms.{Environment.NewLine}" +
                $"WorkingDirectory: {workingDirectory}{Environment.NewLine}" +
                $"Arguments: {string.Join(" ", arguments)}{Environment.NewLine}" +
                $"Captured stdout:{Environment.NewLine}{stdout}{Environment.NewLine}" +
                $"Captured stderr:{Environment.NewLine}{stderr}");
        }

        LogProgress($"[node] exitCode={process.ExitCode}, elapsedMs={startedAt.ElapsedMilliseconds}");

        process.ExitCode.Should().Be(
            0,
            $"node command should succeed.{Environment.NewLine}stdout:{Environment.NewLine}{stdout}{Environment.NewLine}stderr:{Environment.NewLine}{stderr}");
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
            "Set HUSH_SERVER_NODE_ROOT in CI or provide a checkout layout containing Node/HushServerNode.sln. " +
            $"Attempted: {string.Join(", ", attemptedPaths.Distinct())}");
    }

    private static string ResolveWebClientRoot(string serverRepositoryRoot)
    {
        var attemptedPaths = new List<string>();

        var explicitWebClientRoot = Environment.GetEnvironmentVariable("HUSH_WEB_CLIENT_ROOT");
        if (TryResolveWebClientRoot(explicitWebClientRoot, attemptedPaths, out var resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        var nestedWebClientRoot = Path.Combine(serverRepositoryRoot, "hush-web-client");
        if (TryResolveWebClientRoot(nestedWebClientRoot, attemptedPaths, out resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        var siblingWebClientRoot = Path.Combine(
            Directory.GetParent(serverRepositoryRoot)?.FullName ?? serverRepositoryRoot,
            "hush-web-client");
        if (TryResolveWebClientRoot(siblingWebClientRoot, attemptedPaths, out resolvedWebClientRoot))
        {
            return resolvedWebClientRoot;
        }

        throw new InvalidOperationException(
            "Unable to resolve hush-web-client root from test runtime. " +
            "Set HUSH_WEB_CLIENT_ROOT in CI or provide a checkout layout containing the client repository. " +
            $"Attempted: {string.Join(", ", attemptedPaths.Distinct())}");
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

        if (!File.Exists(Path.Combine(fullPath, "scripts", "benchmark-reaction-proof.mjs")))
        {
            return false;
        }

        resolvedRoot = fullPath;
        return true;
    }

    private static byte[] PackProof(SnarkJsProof proof)
    {
        var bytes = new byte[256];
        var offset = 0;

        void WriteField(string value)
        {
            var fieldBytes = System.Numerics.BigInteger.Parse(value).ToByteArray(isUnsigned: true, isBigEndian: true);
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
        [property: JsonPropertyName("protocol")]
        string? Protocol,
        [property: JsonPropertyName("curve")]
        string? Curve);
}
