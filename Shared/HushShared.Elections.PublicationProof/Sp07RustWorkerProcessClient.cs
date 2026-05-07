using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HushShared.Elections.PublicationProof;

public interface ISp07RustWorkerProcessRunner
{
    Task<Sp07RustWorkerProcessResult> RunAsync(
        Sp07RustWorkerProcessInvocation invocation,
        CancellationToken cancellationToken);
}

public interface ISp07RustWorkerClient
{
    Task<Sp07RustWorkerCommandResult> ProveAsync(
        Sp07RustWorkerProofJob job,
        CancellationToken cancellationToken = default);

    Task<Sp07RustWorkerCommandResult> VerifyAsync(
        Sp07RustWorkerVerifyJob job,
        CancellationToken cancellationToken = default);
}

public sealed class Sp07RustWorkerProcessClient : ISp07RustWorkerClient
{
    private const string ExpectedResultSchema = "HushSp07RustWorkerCommandResultV1";
    private const string ExpectedProofProfile = "matrix_m_1_publication_proof_v1";
    private const string SuccessResultCode = "PUB-005";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private readonly Sp07RustWorkerProcessOptions _options;
    private readonly ISp07RustWorkerProcessRunner _runner;

    public Sp07RustWorkerProcessClient(
        Sp07RustWorkerProcessOptions options,
        ISp07RustWorkerProcessRunner? runner = null)
    {
        _options = options;
        _runner = runner ?? new DefaultSp07RustWorkerProcessRunner();
    }

    public async Task<Sp07RustWorkerCommandResult> ProveAsync(
        Sp07RustWorkerProofJob job,
        CancellationToken cancellationToken = default)
    {
        ValidateJob(job);
        Directory.CreateDirectory(job.WorkDirectory);
        if (Path.GetDirectoryName(job.RequestPath) is { Length: > 0 } requestDirectory)
        {
            Directory.CreateDirectory(requestDirectory);
        }

        if (Path.GetDirectoryName(job.ResultPath) is { Length: > 0 } resultDirectory)
        {
            Directory.CreateDirectory(resultDirectory);
        }

        var request = new Sp07RustWorkerProofRequest(
            "HushSp07RustWorkerProofRequestV1",
            job.ElectionId,
            job.ProofSessionId,
            job.ChunkId,
            job.Ballots,
            job.Slots,
            job.IncludeTamperVectors,
            job.IncludeLegacyPhaseArtifacts,
            NormalizeOptional(job.ProtocolPackageHash),
            NormalizeOptional(job.BallotDefinitionHash),
            NormalizeOptional(job.AcceptedBallotSetHash),
            NormalizeOptional(job.PublishedBallotStreamHash),
            job.ProductionProofInput);

        await WriteJsonAtomicAsync(job.RequestPath, request, cancellationToken);
        DeleteIfExists(job.ResultPath);

        var arguments = new List<string>
        {
            "prove",
            "--input",
            job.RequestPath,
            "--output",
            job.ResultPath,
            "--workdir",
            job.WorkDirectory
        };
        AppendThreads(arguments);

        await RunWorkerAsync(arguments, cancellationToken);
        var result = await ReadResultAsync(job.ResultPath, cancellationToken);
        ValidateResult(result, "prove", job.ElectionId, job.ProofSessionId, job.ChunkId);
        return result;
    }

    public async Task<Sp07RustWorkerCommandResult> VerifyAsync(
        Sp07RustWorkerVerifyJob job,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job.InputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.ResultPath);
        if (!File.Exists(job.InputPath))
        {
            throw new FileNotFoundException("SP-07 Rust worker verify input does not exist.", job.InputPath);
        }

        if (Path.GetDirectoryName(job.ResultPath) is { Length: > 0 } resultDirectory)
        {
            Directory.CreateDirectory(resultDirectory);
        }

        DeleteIfExists(job.ResultPath);

        var arguments = new List<string>
        {
            "verify",
            "--input",
            job.InputPath,
            "--output",
            job.ResultPath
        };
        AppendThreads(arguments);

        await RunWorkerAsync(arguments, cancellationToken);
        var result = await ReadResultAsync(job.ResultPath, cancellationToken);
        ValidateResult(result, "verify", job.ElectionId, job.ProofSessionId, job.ChunkId);
        return result;
    }

    private async Task RunWorkerAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var invocation = new Sp07RustWorkerProcessInvocation(
            _options.ExecutablePath,
            ResolveWorkingDirectory(),
            arguments,
            _options.Timeout);

        var processResult = await _runner.RunAsync(invocation, cancellationToken);
        if (processResult.TimedOut)
        {
            throw new Sp07RustWorkerException(
                $"SP-07 Rust worker timed out after {_options.Timeout.TotalSeconds:0.###} seconds.");
        }

        if (processResult.ExitCode != 0)
        {
            throw new Sp07RustWorkerException(
                $"SP-07 Rust worker exited with code {processResult.ExitCode}. stderr: {processResult.StandardError}");
        }
    }

    private async Task<Sp07RustWorkerCommandResult> ReadResultAsync(
        string resultPath,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(resultPath))
        {
            throw new Sp07RustWorkerException($"SP-07 Rust worker did not produce result file: {resultPath}");
        }

        try
        {
            await using var stream = File.OpenRead(resultPath);
            return await JsonSerializer.DeserializeAsync<Sp07RustWorkerCommandResult>(
                    stream,
                    JsonOptions,
                    cancellationToken)
                ?? throw new Sp07RustWorkerException("SP-07 Rust worker result file is empty.");
        }
        catch (JsonException ex)
        {
            throw new Sp07RustWorkerException("SP-07 Rust worker result file is not valid JSON.", ex);
        }
    }

    private static void ValidateJob(Sp07RustWorkerProofJob job)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(job.ElectionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.ProofSessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.ChunkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.WorkDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.RequestPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(job.ResultPath);
        ValidateOptionalHashBinding(job.ProtocolPackageHash, nameof(job.ProtocolPackageHash));
        ValidateOptionalHashBinding(job.BallotDefinitionHash, nameof(job.BallotDefinitionHash));
        ValidateOptionalHashBinding(job.AcceptedBallotSetHash, nameof(job.AcceptedBallotSetHash));
        ValidateOptionalHashBinding(job.PublishedBallotStreamHash, nameof(job.PublishedBallotStreamHash));
        ValidateProductionProofInput(job);
        if (job.Ballots <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(job), "SP-07 proof job ballots must be positive.");
        }

        if (job.Slots <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(job), "SP-07 proof job slots must be positive.");
        }
    }

    private static void ValidateProductionProofInput(Sp07RustWorkerProofJob job)
    {
        if (job.ProductionProofInput is not { } input)
        {
            return;
        }

        if (input.AcceptedBallots.Count != job.Ballots ||
            input.PublishedBallots.Count != job.Ballots ||
            input.PublishedToAccepted.Count != job.Ballots ||
            input.RerandomizationByPublishedBallotAndSlot.Count != job.Ballots)
        {
            throw new Sp07RustWorkerException(
                "SP-07 production proof input ballot counts must match the proof job ballot count.");
        }

        if (input.PublishedToAccepted.Any(index => index < 0 || index >= job.Ballots) ||
            input.PublishedToAccepted.Distinct().Count() != job.Ballots)
        {
            throw new Sp07RustWorkerException(
                "SP-07 production proof input published-to-accepted map must be a full permutation.");
        }

        foreach (var ballot in input.AcceptedBallots.Concat(input.PublishedBallots))
        {
            if (ballot.Slots.Count != job.Slots)
            {
                throw new Sp07RustWorkerException(
                    "SP-07 production proof input ballot slot counts must match the proof job slot count.");
            }
        }

        if (input.RerandomizationByPublishedBallotAndSlot.Any(row => row.Count != job.Slots))
        {
            throw new Sp07RustWorkerException(
                "SP-07 production proof input rerandomization rows must match the proof job slot count.");
        }
    }

    private static void ValidateOptionalHashBinding(string? value, string name)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("SP-07 proof job hash bindings must be null or non-empty.", name);
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static void ValidateResult(
        Sp07RustWorkerCommandResult result,
        string expectedCommand,
        string expectedElectionId,
        string expectedProofSessionId,
        string expectedChunkId)
    {
        if (!string.Equals(result.Schema, ExpectedResultSchema, StringComparison.Ordinal))
        {
            throw new Sp07RustWorkerException($"Unexpected SP-07 Rust worker result schema: {result.Schema}");
        }

        if (!string.Equals(result.Command, expectedCommand, StringComparison.Ordinal))
        {
            throw new Sp07RustWorkerException($"Unexpected SP-07 Rust worker command result: {result.Command}");
        }

        if (!string.Equals(result.ElectionId, expectedElectionId, StringComparison.Ordinal) ||
            !string.Equals(result.ProofSessionId, expectedProofSessionId, StringComparison.Ordinal) ||
            !string.Equals(result.ChunkId, expectedChunkId, StringComparison.Ordinal))
        {
            throw new Sp07RustWorkerException("SP-07 Rust worker result does not match the requested election/session/chunk.");
        }

        if (!string.Equals(result.ProofProfileId, ExpectedProofProfile, StringComparison.Ordinal))
        {
            throw new Sp07RustWorkerException($"Unexpected SP-07 proof profile: {result.ProofProfileId}");
        }

        if (result.Passed)
        {
            if (!string.Equals(result.ResultCode, SuccessResultCode, StringComparison.Ordinal))
            {
                throw new Sp07RustWorkerException($"Passed SP-07 worker result used unexpected code: {result.ResultCode}");
            }

            if (result.StatementHashSha512.Length != 128 ||
                result.TranscriptHashSha512.Length != 128 ||
                result.ProofHashSha512.Length != 128)
            {
                throw new Sp07RustWorkerException("Passed SP-07 worker result is missing required SHA-512 hashes.");
            }

            if (string.IsNullOrWhiteSpace(result.AcceptedBallotSetHash) ||
                string.IsNullOrWhiteSpace(result.PublishedBallotStreamHash))
            {
                throw new Sp07RustWorkerException("Passed SP-07 worker result is missing accepted or published statement bindings.");
            }

            ValidateTelemetry(result);
        }
    }

    private static void ValidateTelemetry(Sp07RustWorkerCommandResult result)
    {
        if (result.Telemetry is not { } telemetry)
        {
            throw new Sp07RustWorkerException("Passed SP-07 worker result is missing required telemetry.");
        }

        if (telemetry.GenerationMilliseconds < 0 ||
            telemetry.SelfVerificationMilliseconds < 0 ||
            telemetry.CpuTimeMilliseconds < 0 ||
            telemetry.ProofSizeBytes <= 0)
        {
            throw new Sp07RustWorkerException("Passed SP-07 worker result contains invalid telemetry values.");
        }

        if (telemetry.ProofSizeBytes != result.CanonicalProofByteLength)
        {
            throw new Sp07RustWorkerException(
                "Passed SP-07 worker telemetry proof size does not match canonical proof byte length.");
        }

        if (telemetry.MemoryNotes.Count == 0)
        {
            throw new Sp07RustWorkerException("Passed SP-07 worker result must include non-secret memory notes.");
        }
    }

    private void AppendThreads(ICollection<string> arguments)
    {
        if (_options.Threads is not { } threads)
        {
            return;
        }

        if (threads <= 0)
        {
            throw new Sp07RustWorkerException("SP-07 Rust worker thread count must be positive when configured.");
        }

        arguments.Add("--threads");
        arguments.Add(threads.ToString());
    }

    private string ResolveWorkingDirectory()
    {
        if (!string.IsNullOrWhiteSpace(_options.WorkingDirectory))
        {
            return _options.WorkingDirectory;
        }

        return Path.GetDirectoryName(_options.ExecutablePath) ?? Environment.CurrentDirectory;
    }

    private static async Task WriteJsonAtomicAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(value, JsonOptions) + Environment.NewLine,
            cancellationToken);
        File.Move(temporaryPath, path, true);
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

public sealed class DefaultSp07RustWorkerProcessRunner : ISp07RustWorkerProcessRunner
{
    public async Task<Sp07RustWorkerProcessResult> RunAsync(
        Sp07RustWorkerProcessInvocation invocation,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = invocation.ExecutablePath,
            WorkingDirectory = invocation.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        foreach (var argument in invocation.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start())
        {
            throw new Sp07RustWorkerException("SP-07 Rust worker process failed to start.");
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(invocation.Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return new Sp07RustWorkerProcessResult(
                -1,
                await CompleteOutputAsync(stdoutTask),
                await CompleteOutputAsync(stderrTask),
                true);
        }

        return new Sp07RustWorkerProcessResult(
            process.ExitCode,
            await stdoutTask,
            await stderrTask,
            false);
    }

    private static async Task<string> CompleteOutputAsync(Task<string> outputTask)
    {
        try
        {
            return await outputTask;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static void TryKill(Process process)
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
            // Timeout handling must not hide the original timeout result.
        }
    }
}
