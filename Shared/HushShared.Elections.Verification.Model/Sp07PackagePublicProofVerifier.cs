using System.Text.Json;
using HushShared.Elections.PublicationProof;

namespace HushShared.Elections.Verification.Model;

public sealed record Sp07PackagePublicProofVerificationRequest(
    string ElectionId,
    string ProofSessionId,
    string ChunkId,
    string StatementHashSha512,
    string FiatShamirTranscriptHashSha512,
    string CanonicalProofHashSha512,
    string AcceptedBallotSetHash,
    string PublishedBallotStreamHash,
    int CanonicalProofByteLength,
    string CanonicalProofBytesHex);

public sealed record Sp07PackagePublicProofVerificationResult(
    bool Passed,
    string ResultCode,
    string Message,
    IReadOnlyDictionary<string, string> Evidence);

public interface ISp07PackagePublicProofVerifier
{
    Task<Sp07PackagePublicProofVerificationResult> VerifyAsync(
        Sp07PackagePublicProofVerificationRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class EnvironmentSp07RustPackagePublicProofVerifier : ISp07PackagePublicProofVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        WriteIndented = true
    };

    public async Task<Sp07PackagePublicProofVerificationResult> VerifyAsync(
        Sp07PackagePublicProofVerificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        Sp07RustWorkerProcessOptions options;
        try
        {
            options = Sp07RustWorkerProcessOptions.FromEnvironment(
                defaultTimeout: TimeSpan.FromMinutes(2));
        }
        catch (Sp07RustWorkerException exception)
        {
            return Failed(
                "PUB-015",
                $"SP-07 Rust public verifier is not available: {exception.Message}");
        }

        var workRoot = Path.Combine(
            Path.GetTempPath(),
            "hush-sp07-package-verify",
            Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(workRoot);
            var inputPath = Path.Combine(workRoot, "verify-input.json");
            var outputPath = Path.Combine(workRoot, "verify-output.json");
            await File.WriteAllTextAsync(
                inputPath,
                JsonSerializer.Serialize(BuildWorkerVerifyInput(request), JsonOptions),
                cancellationToken);

            var client = new Sp07RustWorkerProcessClient(options with { WorkingDirectory = workRoot });
            var result = await client.VerifyAsync(
                new Sp07RustWorkerVerifyJob(
                    request.ElectionId,
                    request.ProofSessionId,
                    request.ChunkId,
                    inputPath,
                    outputPath),
                cancellationToken);

            return new Sp07PackagePublicProofVerificationResult(
                result.Passed,
                result.ResultCode,
                result.Message,
                new Dictionary<string, string>
                {
                    ["worker_kind"] = result.WorkerKind,
                    ["worker_version"] = result.WorkerVersion,
                    ["worker_thread_count"] = result.WorkerThreadCount.ToString(),
                    ["statement_hash_sha512"] = result.StatementHashSha512,
                    ["transcript_hash_sha512"] = result.TranscriptHashSha512,
                    ["proof_hash_sha512"] = result.ProofHashSha512,
                    ["canonical_proof_byte_length"] = result.CanonicalProofByteLength.ToString(),
                });
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Sp07RustWorkerException)
        {
            return Failed(
                "PUB-015",
                $"SP-07 Rust public verifier failed: {exception.Message}");
        }
        finally
        {
            TryDeleteDirectory(workRoot);
        }
    }

    private static object BuildWorkerVerifyInput(Sp07PackagePublicProofVerificationRequest request) =>
        new
        {
            Passed = true,
            ResultCode = "PUB-005",
            ElectionId = request.ElectionId,
            ProofSessionId = request.ProofSessionId,
            ChunkId = request.ChunkId,
            ProofProfileId = "matrix_m_1_publication_proof_v1",
            StatementHashSha512 = request.StatementHashSha512,
            TranscriptHashSha512 = request.FiatShamirTranscriptHashSha512,
            ProofHashSha512 = request.CanonicalProofHashSha512,
            PublishedBallotStreamHash = request.PublishedBallotStreamHash,
            AcceptedBallotSetHash = request.AcceptedBallotSetHash,
            CanonicalProofByteLength = request.CanonicalProofByteLength,
            CanonicalProofBytesHex = request.CanonicalProofBytesHex,
        };

    private static Sp07PackagePublicProofVerificationResult Failed(string resultCode, string message) =>
        new(
            false,
            resultCode,
            message,
            new Dictionary<string, string>());

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
