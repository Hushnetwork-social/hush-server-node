using System.Text.Json.Serialization;

namespace HushShared.Elections.PublicationProof;

public sealed record Sp07RustWorkerProcessOptions(
    string ExecutablePath,
    TimeSpan Timeout,
    string? WorkingDirectory = null,
    int? Threads = null)
{
    public const string WorkerPathEnvironmentVariable = "HUSH_SP07_RUST_WORKER_PATH";
    public const string WorkerThreadsEnvironmentVariable = "HUSH_SP07_RUST_WORKER_THREADS";
    public const string WorkerTimeoutSecondsEnvironmentVariable = "HUSH_SP07_RUST_WORKER_TIMEOUT_SECONDS";

    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(2);

    public static Sp07RustWorkerProcessOptions FromEnvironment(
        TimeSpan? defaultTimeout = null,
        string? defaultWorkingDirectory = null) =>
        FromEnvironmentVariables(
            Environment.GetEnvironmentVariable,
            defaultTimeout,
            defaultWorkingDirectory);

    public static Sp07RustWorkerProcessOptions FromEnvironmentVariables(
        Func<string, string?> readVariable,
        TimeSpan? defaultTimeout = null,
        string? defaultWorkingDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(readVariable);

        var executablePath = NormalizeOptional(readVariable(WorkerPathEnvironmentVariable));
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            throw new Sp07RustWorkerException(
                $"SP-07 Rust worker path is not configured. Set {WorkerPathEnvironmentVariable}.");
        }

        return new Sp07RustWorkerProcessOptions(
            executablePath,
            ResolveTimeout(readVariable, defaultTimeout ?? DefaultTimeout),
            NormalizeOptional(defaultWorkingDirectory),
            ResolveThreads(readVariable));
    }

    private static TimeSpan ResolveTimeout(
        Func<string, string?> readVariable,
        TimeSpan fallback)
    {
        if (fallback <= TimeSpan.Zero)
        {
            throw new Sp07RustWorkerException("SP-07 Rust worker default timeout must be positive.");
        }

        var configured = NormalizeOptional(readVariable(WorkerTimeoutSecondsEnvironmentVariable));
        if (configured is null)
        {
            return fallback;
        }

        if (!double.TryParse(
                configured,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out var seconds) ||
            seconds <= 0)
        {
            throw new Sp07RustWorkerException(
                $"{WorkerTimeoutSecondsEnvironmentVariable} must be a positive number of seconds.");
        }

        return TimeSpan.FromSeconds(seconds);
    }

    private static int? ResolveThreads(Func<string, string?> readVariable)
    {
        var configured = NormalizeOptional(readVariable(WorkerThreadsEnvironmentVariable));
        if (configured is null)
        {
            return null;
        }

        if (!int.TryParse(
                configured,
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture,
                out var threads) ||
            threads <= 0)
        {
            throw new Sp07RustWorkerException(
                $"{WorkerThreadsEnvironmentVariable} must be a positive integer when configured.");
        }

        return threads;
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }
}

public sealed record Sp07RustWorkerProofJob(
    string ElectionId,
    string ProofSessionId,
    string ChunkId,
    int Ballots,
    int Slots,
    string WorkDirectory,
    string RequestPath,
    string ResultPath,
    bool IncludeTamperVectors = false,
    bool IncludeLegacyPhaseArtifacts = false,
    string? ProtocolPackageHash = null,
    string? BallotDefinitionHash = null,
    string? AcceptedBallotSetHash = null,
    string? PublishedBallotStreamHash = null,
    Sp07RustWorkerProductionProofInput? ProductionProofInput = null);

public sealed record Sp07RustWorkerProductionProofInput(
    Sp07PointPayload PublicKey,
    IReadOnlyList<Sp07CipherBallotPayload> AcceptedBallots,
    IReadOnlyList<Sp07CipherBallotPayload> PublishedBallots,
    IReadOnlyList<int> PublishedToAccepted,
    IReadOnlyList<IReadOnlyList<string>> RerandomizationByPublishedBallotAndSlot);

public sealed record Sp07RustWorkerVerifyJob(
    string ElectionId,
    string ProofSessionId,
    string ChunkId,
    string InputPath,
    string ResultPath);

public sealed record Sp07RustWorkerProofRequest(
    string Schema,
    string ElectionId,
    string ProofSessionId,
    string ChunkId,
    int Ballots,
    int Slots,
    bool IncludeTamperVectors,
    bool IncludeLegacyPhaseArtifacts,
    string? ProtocolPackageHash,
    string? BallotDefinitionHash,
    string? AcceptedBallotSetHash,
    string? PublishedBallotStreamHash,
    Sp07RustWorkerProductionProofInput? ProductionProofInput);

public sealed record Sp07RustWorkerCommandResult(
    [property: JsonPropertyName("schema")] string Schema,
    [property: JsonPropertyName("worker_kind")] string WorkerKind,
    [property: JsonPropertyName("command")] string Command,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("passed")] bool Passed,
    [property: JsonPropertyName("result_code")] string ResultCode,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("election_id")] string ElectionId,
    [property: JsonPropertyName("proof_session_id")] string ProofSessionId,
    [property: JsonPropertyName("chunk_id")] string ChunkId,
    [property: JsonPropertyName("proof_profile_id")] string ProofProfileId,
    [property: JsonPropertyName("worker_version")] string WorkerVersion,
    [property: JsonPropertyName("worker_thread_count")] int WorkerThreadCount,
    [property: JsonPropertyName("statement_hash_sha512")] string StatementHashSha512,
    [property: JsonPropertyName("transcript_hash_sha512")] string TranscriptHashSha512,
    [property: JsonPropertyName("proof_hash_sha512")] string ProofHashSha512,
    [property: JsonPropertyName("accepted_ballot_set_hash")] string AcceptedBallotSetHash,
    [property: JsonPropertyName("published_ballot_stream_hash")] string PublishedBallotStreamHash,
    [property: JsonPropertyName("canonical_proof_byte_length")] int CanonicalProofByteLength,
    [property: JsonPropertyName("canonical_proof_bytes_hex")] string? CanonicalProofBytesHex,
    [property: JsonPropertyName("proof_example_hash_sha512")] string ProofExampleHashSha512,
    [property: JsonPropertyName("elapsed_milliseconds")] double ElapsedMilliseconds,
    [property: JsonPropertyName("telemetry")] Sp07RustWorkerTelemetry? Telemetry = null);

public sealed record Sp07RustWorkerTelemetry(
    [property: JsonPropertyName("generation_milliseconds")] double GenerationMilliseconds,
    [property: JsonPropertyName("self_verification_milliseconds")] double SelfVerificationMilliseconds,
    [property: JsonPropertyName("proof_size_bytes")] int ProofSizeBytes,
    [property: JsonPropertyName("cpu_time_milliseconds")] double? CpuTimeMilliseconds,
    IReadOnlyList<string>? MemoryNotes = null,
    IReadOnlyDictionary<string, double>? PhaseTimings = null)
{
    [JsonPropertyName("memory_notes")]
    public IReadOnlyList<string> MemoryNotes { get; init; } =
        MemoryNotes?
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.Trim())
            .ToArray() ?? [];

    [JsonPropertyName("phase_timings")]
    public IReadOnlyDictionary<string, double> PhaseTimings { get; init; } =
        PhaseTimings is null
            ? new Dictionary<string, double>()
            : new Dictionary<string, double>(PhaseTimings, StringComparer.Ordinal);
}

public sealed record Sp07RustWorkerProcessInvocation(
    string ExecutablePath,
    string WorkingDirectory,
    IReadOnlyList<string> Arguments,
    TimeSpan Timeout);

public sealed record Sp07RustWorkerProcessResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    bool TimedOut);

public class Sp07PublicationProofException : Exception
{
    public Sp07PublicationProofException(string message)
        : base(message)
    {
    }

    public Sp07PublicationProofException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

public sealed class Sp07RustWorkerException : Sp07PublicationProofException
{
    public Sp07RustWorkerException(string message)
        : base(message)
    {
    }

    public Sp07RustWorkerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
