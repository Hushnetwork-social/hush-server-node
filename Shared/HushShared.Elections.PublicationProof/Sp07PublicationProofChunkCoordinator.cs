using System.Diagnostics;

namespace HushShared.Elections.PublicationProof;

public sealed record Sp07PublicationProofChunkCoordinatorOptions(
    string WorkRoot,
    int MaxParallelWorkers = 1,
    bool VerifyAfterProve = true,
    double MaxApprovedChunkProofMilliseconds = 5000)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(WorkRoot))
        {
            throw new Sp07PublicationProofException("SP-07 proof coordinator work root is required.");
        }

        if (MaxParallelWorkers < 1)
        {
            throw new Sp07PublicationProofException("SP-07 proof coordinator max parallel workers must be positive.");
        }

        if (MaxApprovedChunkProofMilliseconds <= 0)
        {
            throw new Sp07PublicationProofException(
                "SP-07 proof coordinator approved chunk proof ceiling must be positive.");
        }
    }
}

public sealed record Sp07PublicationProofStatementBinding(
    string? ProtocolPackageHash = null,
    string? BallotDefinitionHash = null,
    string? AcceptedBallotSetHash = null,
    string? PublishedBallotStreamHash = null);

public sealed record Sp07PublicationProofSessionRunResult(
    string ElectionId,
    string ProofSessionId,
    string PlanId,
    bool Passed,
    int ChunkCount,
    int CompletedChunkCount,
    int FailedChunkCount,
    double SlowestChunkMilliseconds,
    IReadOnlyList<Sp07PublicationProofChunkRunResult> Chunks);

public sealed record Sp07PublicationProofChunkRunResult(
    string ChunkId,
    int ChunkIndex,
    int Offset,
    int Count,
    bool Passed,
    string WorkDirectory,
    string RequestPath,
    string ProofResultPath,
    string VerifyResultPath,
    Sp07RustWorkerCommandResult? ProofResult,
    Sp07RustWorkerCommandResult? VerifyResult,
    double ElapsedMilliseconds,
    string? FailureCode = null,
    string? FailureMessage = null);

public sealed class Sp07PublicationProofChunkCoordinator(
    ISp07RustWorkerClient workerClient,
    Sp07PublicationProofChunkCoordinatorOptions options)
{
    private readonly ISp07RustWorkerClient _workerClient = workerClient;
    private readonly Sp07PublicationProofChunkCoordinatorOptions _options = options;

    public async Task<Sp07PublicationProofSessionRunResult> RunAsync(
        string electionId,
        string proofSessionId,
        Sp07PublicationChunkPlan plan,
        Sp07PublicationProofStatementBinding? statementBinding = null,
        IReadOnlyDictionary<string, Sp07RustWorkerProductionProofInput>? productionProofInputsByChunkId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(electionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proofSessionId);
        _options.Validate();

        if (plan.Chunks.Count == 0)
        {
            throw new Sp07PublicationProofException("SP-07 proof coordinator requires at least one chunk.");
        }

        Directory.CreateDirectory(_options.WorkRoot);
        using var semaphore = new SemaphoreSlim(Math.Min(_options.MaxParallelWorkers, plan.Chunks.Count));
        var tasks = plan.Chunks
            .Select(chunk => RunChunkAsync(
                electionId,
                proofSessionId,
                plan,
                chunk,
                statementBinding,
                productionProofInputsByChunkId,
                semaphore,
                cancellationToken))
            .ToArray();
        var chunks = await Task.WhenAll(tasks);
        var orderedChunks = chunks.OrderBy(chunk => chunk.ChunkIndex).ToArray();

        return new Sp07PublicationProofSessionRunResult(
            electionId,
            proofSessionId,
            plan.PlanId,
            orderedChunks.All(chunk => chunk.Passed),
            orderedChunks.Length,
            orderedChunks.Count(chunk => chunk.ProofResult is not null),
            orderedChunks.Count(chunk => !chunk.Passed),
            orderedChunks.Length == 0 ? 0 : orderedChunks.Max(chunk => chunk.ElapsedMilliseconds),
            orderedChunks);
    }

    private async Task<Sp07PublicationProofChunkRunResult> RunChunkAsync(
        string electionId,
        string proofSessionId,
        Sp07PublicationChunkPlan plan,
        Sp07PublicationChunkPlanItem chunk,
        Sp07PublicationProofStatementBinding? statementBinding,
        IReadOnlyDictionary<string, Sp07RustWorkerProductionProofInput>? productionProofInputsByChunkId,
        SemaphoreSlim semaphore,
        CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            return await RunChunkCoreAsync(
                electionId,
                proofSessionId,
                plan,
                chunk,
                statementBinding,
                productionProofInputsByChunkId,
                cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<Sp07PublicationProofChunkRunResult> RunChunkCoreAsync(
        string electionId,
        string proofSessionId,
        Sp07PublicationChunkPlan plan,
        Sp07PublicationChunkPlanItem chunk,
        Sp07PublicationProofStatementBinding? statementBinding,
        IReadOnlyDictionary<string, Sp07RustWorkerProductionProofInput>? productionProofInputsByChunkId,
        CancellationToken cancellationToken)
    {
        var workDirectory = GetChunkWorkDirectory(electionId, proofSessionId, chunk);
        var requestPath = Path.Combine(workDirectory, "proof-request.json");
        var proofResultPath = Path.Combine(workDirectory, "proof-result.json");
        var verifyResultPath = Path.Combine(workDirectory, "verify-result.json");
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Sp07RustWorkerProductionProofInput? productionProofInput = null;
            if (productionProofInputsByChunkId is not null &&
                !productionProofInputsByChunkId.TryGetValue(chunk.ChunkId, out productionProofInput))
            {
                throw new Sp07PublicationProofException(
                    $"SP-07 proof coordinator did not receive production proof input for chunk {chunk.ChunkId}.");
            }

            var proof = await _workerClient.ProveAsync(
                new Sp07RustWorkerProofJob(
                    electionId,
                    proofSessionId,
                    chunk.ChunkId,
                    chunk.Count,
                    plan.EncryptedSlotCount,
                    workDirectory,
                    requestPath,
                    proofResultPath,
                    ProtocolPackageHash: statementBinding?.ProtocolPackageHash,
                    BallotDefinitionHash: statementBinding?.BallotDefinitionHash,
                    AcceptedBallotSetHash: statementBinding?.AcceptedBallotSetHash,
                    PublishedBallotStreamHash: statementBinding?.PublishedBallotStreamHash,
                    ProductionProofInput: productionProofInput),
                cancellationToken);
            Sp07RustWorkerCommandResult? verify = null;
            if (_options.VerifyAfterProve)
            {
                verify = await _workerClient.VerifyAsync(
                    new Sp07RustWorkerVerifyJob(
                        electionId,
                        proofSessionId,
                        chunk.ChunkId,
                        proofResultPath,
                        verifyResultPath),
                    cancellationToken);
            }

            var proofMilliseconds = ResolveProofMilliseconds(proof);
            if (proof.Passed && proofMilliseconds > _options.MaxApprovedChunkProofMilliseconds)
            {
                stopwatch.Stop();
                return new Sp07PublicationProofChunkRunResult(
                    chunk.ChunkId,
                    chunk.ChunkIndex,
                    chunk.Offset,
                    chunk.Count,
                    false,
                    workDirectory,
                    requestPath,
                    proofResultPath,
                    verifyResultPath,
                    proof,
                    verify,
                    stopwatch.Elapsed.TotalMilliseconds,
                    "sp07_chunk_performance_target_missed",
                    $"SP-07 proof chunk {chunk.ChunkId} reported {proofMilliseconds:0.###}ms generation plus self-verification, above the approved {_options.MaxApprovedChunkProofMilliseconds:0.###}ms ceiling.");
            }

            stopwatch.Stop();
            return new Sp07PublicationProofChunkRunResult(
                chunk.ChunkId,
                chunk.ChunkIndex,
                chunk.Offset,
                chunk.Count,
                proof.Passed && (verify?.Passed ?? true),
                workDirectory,
                requestPath,
                proofResultPath,
                verifyResultPath,
                proof,
                verify,
                stopwatch.Elapsed.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is Sp07PublicationProofException or IOException or UnauthorizedAccessException)
        {
            stopwatch.Stop();
            return new Sp07PublicationProofChunkRunResult(
                chunk.ChunkId,
                chunk.ChunkIndex,
                chunk.Offset,
                chunk.Count,
                false,
                workDirectory,
                requestPath,
                proofResultPath,
                verifyResultPath,
                null,
                null,
                stopwatch.Elapsed.TotalMilliseconds,
                "sp07_chunk_worker_failed",
                ex.Message);
        }
    }

    private string GetChunkWorkDirectory(
        string electionId,
        string proofSessionId,
        Sp07PublicationChunkPlanItem chunk) =>
        Path.Combine(
            _options.WorkRoot,
            SanitizePathSegment(electionId),
            SanitizePathSegment(proofSessionId),
            SanitizePathSegment(chunk.ChunkId));

    private static string SanitizePathSegment(string value)
    {
        var chars = value.Select(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_'
                ? character
                : '_');
        var sanitized = new string(chars.ToArray()).Trim('_');
        return string.IsNullOrWhiteSpace(sanitized) ? "sp07" : sanitized;
    }

    private static double ResolveProofMilliseconds(Sp07RustWorkerCommandResult proof)
    {
        if (proof.Telemetry is { } telemetry)
        {
            return telemetry.GenerationMilliseconds + telemetry.SelfVerificationMilliseconds;
        }

        return proof.ElapsedMilliseconds;
    }
}
