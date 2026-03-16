using System.Diagnostics;
using System.Text.Json;
using System.Text;

namespace HushNode.Reactions.ZK;

public static class SnarkJsProcessGroth16Verifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    public static async Task<bool> VerifyAsync(
        byte[] packedProof,
        string[] publicSignals,
        string verificationKeyPath,
        string nodeExecutable,
        string verifierScriptPath,
        int timeoutMs,
        CancellationToken cancellationToken = default,
        Action<string>? onLog = null)
    {
        if (!File.Exists(verificationKeyPath))
        {
            throw new FileNotFoundException("Verification key not found.", verificationKeyPath);
        }

        if (!File.Exists(verifierScriptPath))
        {
            throw new FileNotFoundException("snarkjs verifier script not found.", verifierScriptPath);
        }

        var tempDirectory = Path.Combine(Path.GetTempPath(), $"feat087-snarkjs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);

        try
        {
            var proofPath = Path.Combine(tempDirectory, "proof.json");
            var publicSignalsPath = Path.Combine(tempDirectory, "public-signals.json");

            await File.WriteAllTextAsync(
                proofPath,
                JsonSerializer.Serialize(PackedGroth16ProofAdapter.Unpack(packedProof), JsonOptions),
                cancellationToken);
            await File.WriteAllTextAsync(
                publicSignalsPath,
                JsonSerializer.Serialize(publicSignals, JsonOptions),
                cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = nodeExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            startInfo.ArgumentList.Add(verifierScriptPath);
            startInfo.ArgumentList.Add(verificationKeyPath);
            startInfo.ArgumentList.Add(proofPath);
            startInfo.ArgumentList.Add(publicSignalsPath);
            startInfo.Environment["SNARKJS_DEBUG"] = "true";

            using var process = new Process { StartInfo = startInfo };
            var stopwatch = Stopwatch.StartNew();
            var stderrBuffer = new StringBuilder();

            process.ErrorDataReceived += (_, eventArgs) =>
            {
                if (eventArgs.Data is null)
                {
                    return;
                }

                lock (stderrBuffer)
                {
                    stderrBuffer.AppendLine(eventArgs.Data);
                }

                onLog?.Invoke(eventArgs.Data);
            };

            process.Start();
            process.BeginErrorReadLine();

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(timeoutMs);

            try
            {
                await process.WaitForExitAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                var timeoutStdout = await stdoutTask;
                var timeoutStderr = stderrBuffer.ToString();
                stopwatch.Stop();

                if (process.HasExited)
                {
                    if (process.ExitCode != 0)
                    {
                        throw new InvalidOperationException(
                            BuildDiagnosticMessage(
                                $"snarkjs verifier process exited with code {process.ExitCode} after timeout cancellation",
                                process,
                                stopwatch.ElapsedMilliseconds,
                                nodeExecutable,
                                verifierScriptPath,
                                verificationKeyPath,
                                proofPath,
                                publicSignalsPath,
                                timeoutStdout,
                                timeoutStderr));
                    }

                    var timedOutResponse = JsonSerializer.Deserialize<SnarkJsVerifierResponse>(timeoutStdout, JsonOptions);
                    if (timedOutResponse is not null && string.IsNullOrWhiteSpace(timedOutResponse.Error))
                    {
                        return timedOutResponse.Valid;
                    }
                }

                TryKillProcess(process);
                throw new TimeoutException(BuildDiagnosticMessage(
                    "snarkjs verifier process timed out",
                    process,
                    stopwatch.ElapsedMilliseconds,
                    nodeExecutable,
                    verifierScriptPath,
                    verificationKeyPath,
                    proofPath,
                    publicSignalsPath,
                    timeoutStdout,
                    timeoutStderr));
            }

            var stdout = await stdoutTask;
            var stderr = stderrBuffer.ToString();
            stopwatch.Stop();

            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    BuildDiagnosticMessage(
                        $"snarkjs verifier process failed with exit code {process.ExitCode}",
                        process,
                        stopwatch.ElapsedMilliseconds,
                        nodeExecutable,
                        verifierScriptPath,
                        verificationKeyPath,
                        proofPath,
                        publicSignalsPath,
                        stdout,
                        stderr));
            }

            var response = JsonSerializer.Deserialize<SnarkJsVerifierResponse>(stdout, JsonOptions)
                ?? throw new InvalidOperationException(
                    BuildDiagnosticMessage(
                        "snarkjs verifier returned empty output",
                        process,
                        stopwatch.ElapsedMilliseconds,
                        nodeExecutable,
                        verifierScriptPath,
                        verificationKeyPath,
                        proofPath,
                        publicSignalsPath,
                        stdout,
                        stderr));

            if (!string.IsNullOrWhiteSpace(response.Error))
            {
                throw new InvalidOperationException(
                    BuildDiagnosticMessage(
                        $"snarkjs verifier error: {response.Error}",
                        process,
                        stopwatch.ElapsedMilliseconds,
                        nodeExecutable,
                        verifierScriptPath,
                        verificationKeyPath,
                        proofPath,
                        publicSignalsPath,
                        stdout,
                        stderr));
            }

            return response.Valid;
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    private sealed record SnarkJsVerifierResponse(bool Valid, string? Error);

    private static void TryKillProcess(Process process)
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
            // Best effort during timeout cleanup.
        }
    }

    private static string BuildDiagnosticMessage(
        string message,
        Process process,
        long elapsedMs,
        string nodeExecutable,
        string verifierScriptPath,
        string verificationKeyPath,
        string proofPath,
        string publicSignalsPath,
        string stdout,
        string stderr)
    {
        var builder = new StringBuilder();
        builder.Append(message);
        builder.Append(". ");
        builder.Append($"elapsedMs={elapsedMs}; ");
        builder.Append($"pid={(process.HasExited ? "exited" : process.Id.ToString())}; ");
        builder.Append($"nodeExecutable={nodeExecutable}; ");
        builder.Append($"verifierScriptPath={verifierScriptPath}; ");
        builder.Append($"verificationKeyPath={verificationKeyPath}; ");
        builder.Append($"proofPath={proofPath}; ");
        builder.Append($"publicSignalsPath={publicSignalsPath}; ");
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            builder.Append($"stdout={stdout.Trim()}; ");
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            builder.Append($"stderr={stderr.Trim()}; ");
        }

        return builder.ToString().Trim();
    }
}
