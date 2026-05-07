using HushNode.Elections.Storage;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;
using HushShared.Elections.Verification.Model;

namespace HushNode.Elections;

public interface IElectionSp07PublicationProofSessionRunner
{
    Task<ElectionSp07PublicationProofSessionRunnerResult> RunAsync(
        ElectionSp07PublicationProofSessionRunnerRequest request,
        CancellationToken cancellationToken = default);
}

public sealed record ElectionSp07PublicationProofSessionRunnerRequest(
    IElectionsRepository Repository,
    ElectionRecord Election,
    ProtocolPackageBindingRecord? ProtocolPackageBinding,
    string ProfileId,
    DateTime StartedAt);

public sealed record ElectionSp07PublicationProofSessionRunnerResult(
    bool IsSuccessful,
    string? FailureCode,
    string? FailureReason,
    ElectionPublicationProofSessionRecord? Session,
    ElectionPublicationProofTranscriptRecord? Transcript,
    Sp07PublicationProofSessionRunResult? WorkerResult)
{
    public static ElectionSp07PublicationProofSessionRunnerResult Success(
        ElectionPublicationProofSessionRecord session,
        ElectionPublicationProofTranscriptRecord transcript,
        Sp07PublicationProofSessionRunResult workerResult) =>
        new(true, null, null, session, transcript, workerResult);

    public static ElectionSp07PublicationProofSessionRunnerResult Failure(
        string failureCode,
        string failureReason,
        ElectionPublicationProofSessionRecord? session = null,
        Sp07PublicationProofSessionRunResult? workerResult = null) =>
        new(false, failureCode, failureReason, session, null, workerResult);
}

public sealed class ElectionSp07PublicationProofSessionRunner(
    IElectionSp07ProductionProofInputBuilder productionProofInputBuilder,
    Sp07PublicationProofChunkCoordinator proofChunkCoordinator,
    IElectionSp07PublicationProofManifestBuilder manifestBuilder) : IElectionSp07PublicationProofSessionRunner
{
    private const string BallotEncryptionSchemeVersion = "babyjubjub-elgamal-vector-ballot-v1";

    private readonly IElectionSp07ProductionProofInputBuilder _productionProofInputBuilder =
        productionProofInputBuilder ?? throw new ArgumentNullException(nameof(productionProofInputBuilder));
    private readonly Sp07PublicationProofChunkCoordinator _proofChunkCoordinator =
        proofChunkCoordinator ?? throw new ArgumentNullException(nameof(proofChunkCoordinator));
    private readonly IElectionSp07PublicationProofManifestBuilder _manifestBuilder =
        manifestBuilder ?? throw new ArgumentNullException(nameof(manifestBuilder));

    public async Task<ElectionSp07PublicationProofSessionRunnerResult> RunAsync(
        ElectionSp07PublicationProofSessionRunnerRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.Repository);
        ArgumentNullException.ThrowIfNull(request.Election);

        var acceptedBallots = await request.Repository.GetAcceptedBallotsAsync(request.Election.ElectionId);
        var publishedBallots = await request.Repository.GetPublishedBallotsAsync(request.Election.ElectionId);
        if (acceptedBallots.Count == 0 || acceptedBallots.Count != publishedBallots.Count)
        {
            return Failure(
                VerificationResultCodes.PublicationProofCountMismatch,
                "SP-07 proof session requires matching accepted and published ballot counts.");
        }

        var ballotDefinitionHash = ResolveBallotDefinitionHash(request.Election);
        if (ballotDefinitionHash is null)
        {
            return Failure(
                VerificationResultCodes.PublicationProofTranscriptInvalid,
                "SP-07 proof session requires a sealed ballot definition hash.");
        }

        Sp07PublicationChunkPlan plan;
        try
        {
            plan = CreatePlan(acceptedBallots.Count, request.Election.Options.Count);
        }
        catch (Sp07PublicationProofException exception)
        {
            return Failure(
                VerificationResultCodes.PublicationProofVerificationFailed,
                exception.Message);
        }

        var witnesses = (await request.Repository.GetPublicationWitnessesAsync(request.Election.ElectionId))
            .Where(x => x.CustodyStatus == ElectionPublicationWitnessCustodyStatus.Sealed)
            .ToArray();
        var productionInput = _productionProofInputBuilder.Build(
            request.Election.ElectionId,
            acceptedBallots,
            publishedBallots,
            witnesses,
            plan);
        if (!productionInput.IsSuccessful || productionInput.WitnessSetId is null)
        {
            return Failure(
                productionInput.FailureCode ?? VerificationResultCodes.PublicationProofWitnessDeletionInvalid,
                productionInput.FailureReason ?? "SP-07 production proof input could not be built.");
        }

        var acceptedHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputeAcceptedBallotInventoryHash(acceptedBallots));
        var publishedHash = VerificationCanonicalHash.ToLowerHex(
            VerificationCanonicalHash.ComputePublishedBallotStreamHash(publishedBallots));
        var session = new ElectionPublicationProofSessionRecord(
            Guid.NewGuid(),
            request.Election.ElectionId,
            productionInput.WitnessSetId.Value,
            ElectionSp07ProfileIds.PublicationProofMode,
            ElectionSp07ProfileIds.ProofConstruction,
            ElectionSp07ProfileIds.StatementId,
            ElectionPublicationProofSessionStatus.Generating,
            request.StartedAt,
            CompletedAt: null,
            acceptedBallots.Count,
            publishedBallots.Count,
            plan.Chunks.Count,
            RetryCount: 0,
            FailureCode: null,
            FailureReason: null,
            acceptedHash,
            publishedHash,
            TranscriptHash: null,
            ProofHash: null,
            ServerVerifierOutputHash: null,
            DeletionReceiptId: null);
        await request.Repository.SavePublicationProofSessionAsync(session);

        var runResult = await _proofChunkCoordinator.RunAsync(
            request.Election.ElectionId.ToString(),
            session.Id.ToString("N"),
            plan,
            new Sp07PublicationProofStatementBinding(
                ProtocolPackageHash: request.ProtocolPackageBinding?.ReleaseManifestHash,
                BallotDefinitionHash: ballotDefinitionHash,
                AcceptedBallotSetHash: acceptedHash,
                PublishedBallotStreamHash: publishedHash),
            productionInput.InputsByChunkId,
            cancellationToken);

        var completedAt = DateTime.UtcNow;
        if (!runResult.Passed)
        {
            var failedSession = session with
            {
                Status = ElectionPublicationProofSessionStatus.Failed,
                CompletedAt = completedAt,
                FailureCode = ResolveFailureCode(runResult),
                FailureReason = ResolveFailureReason(runResult),
            };
            await request.Repository.UpdatePublicationProofSessionAsync(failedSession);
            return ElectionSp07PublicationProofSessionRunnerResult.Failure(
                failedSession.FailureCode!,
                failedSession.FailureReason!,
                failedSession,
                runResult);
        }

        var transcriptBuild = _manifestBuilder.Build(new ElectionSp07PublicationProofTranscriptBuildRequest(
            request.Election.ElectionId,
            session.Id,
            session.WitnessSetId,
            request.ProfileId,
            ballotDefinitionHash,
            BallotEncryptionSchemeVersion,
            productionInput.ElectionPublicKeyId ?? "sp07-election-public-key-unknown",
            acceptedHash,
            publishedHash,
            acceptedBallots.Count,
            publishedBallots.Count,
            request.Election.Options.Count,
            runResult,
            completedAt,
            GeneratorReleaseHash: request.ProtocolPackageBinding?.ReleaseManifestHash,
            VerifierReleaseHash: request.ProtocolPackageBinding?.ReleaseManifestHash));
        await request.Repository.SavePublicationProofTranscriptAsync(transcriptBuild.Transcript);

        var verifiedSession = session with
        {
            Status = ElectionPublicationProofSessionStatus.Verified,
            CompletedAt = completedAt,
            TranscriptHash = transcriptBuild.TranscriptHash,
            ProofHash = transcriptBuild.ProofHash,
            ServerVerifierOutputHash = VerificationCanonicalHash.ComputeSha256LowerHex(transcriptBuild.ProofBytes),
        };
        await request.Repository.UpdatePublicationProofSessionAsync(verifiedSession);

        return ElectionSp07PublicationProofSessionRunnerResult.Success(
            verifiedSession,
            transcriptBuild.Transcript,
            runResult);
    }

    private static ElectionSp07PublicationProofSessionRunnerResult Failure(
        string failureCode,
        string failureReason) =>
        ElectionSp07PublicationProofSessionRunnerResult.Failure(failureCode, failureReason);

    private static Sp07PublicationChunkPlan CreatePlan(int acceptedBallotCount, int ciphertextSlotCount)
    {
        var options = new Sp07PublicationChunkPlannerOptions(
            MaxBallotsPerChunk: Math.Max(
                1,
                (int)Math.Ceiling(
                    (double)ElectionSp07ProfileIds.HighAssuranceV1MaxAcceptedBallots /
                    ElectionSp07ProfileIds.HighAssuranceV1MaxPublicationChunks)),
            MinBallotsPerChunk: 2,
            MaxChunks: ElectionSp07ProfileIds.HighAssuranceV1MaxPublicationChunks,
            MaxEncryptedSlots: ElectionSp07ProfileIds.HighAssuranceV1MaxEncryptedSlots);

        return new Sp07PublicationChunkPlanner(options)
            .CreatePlan(acceptedBallotCount, ciphertextSlotCount);
    }

    private static string? ResolveBallotDefinitionHash(ElectionRecord election) =>
        election.BallotDefinitionHash is { Length: > 0 }
            ? VerificationCanonicalHash.ToLowerHex(election.BallotDefinitionHash)
            : null;

    private static string ResolveFailureCode(Sp07PublicationProofSessionRunResult runResult) =>
        runResult.Chunks
            .Where(chunk => !chunk.Passed)
            .Select(chunk => chunk.FailureCode ?? chunk.VerifyResult?.ResultCode ?? chunk.ProofResult?.ResultCode)
            .FirstOrDefault(code => !string.IsNullOrWhiteSpace(code)) ??
        VerificationResultCodes.PublicationProofVerificationFailed;

    private static string ResolveFailureReason(Sp07PublicationProofSessionRunResult runResult) =>
        runResult.Chunks
            .Where(chunk => !chunk.Passed)
            .Select(chunk => chunk.FailureMessage ?? chunk.VerifyResult?.Message ?? chunk.ProofResult?.Message)
            .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason)) ??
        "SP-07 proof worker failed one or more publication proof chunks.";
}
