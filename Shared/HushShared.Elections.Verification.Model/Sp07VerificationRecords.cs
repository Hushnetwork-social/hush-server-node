using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public record ElectionSp07PublicationProofTranscriptArtifactRecord(
    string ElectionId,
    string TranscriptVersion,
    string PublicationProofMode,
    string ProofConstruction,
    string StatementId,
    string ProfileId,
    string BallotDefinitionHash,
    string BallotEncryptionSchemeVersion,
    string ElectionPublicKeyId,
    string AcceptedBallotSetHash,
    string PublishedBallotStreamHash,
    int AcceptedBallotCount,
    int PublishedBallotCount,
    int CiphertextSlotCount,
    string ProofSystemVersion,
    string ProofBytes,
    string ProofHash,
    string TranscriptHash,
    string ExternalReviewStatus,
    DateTime GeneratedAt,
    string? GeneratorReleaseHash,
    string? VerifierReleaseHash,
    IReadOnlyList<string> PublicPrivacyBoundary,
    string? StatementHashSha512 = null,
    string? FiatShamirTranscriptHashSha512 = null,
    string? CanonicalProofBytesHex = null,
    string? CanonicalProofHashSha512 = null,
    int? CanonicalProofByteLength = null)
{
    public string ElectionId { get; init; } = NormalizeRequiredValue(ElectionId, nameof(ElectionId));
    public string TranscriptVersion { get; init; } =
        NormalizeRequiredValue(TranscriptVersion, nameof(TranscriptVersion));
    public string PublicationProofMode { get; init; } =
        NormalizeRequiredValue(PublicationProofMode, nameof(PublicationProofMode));
    public string ProofConstruction { get; init; } =
        NormalizeRequiredValue(ProofConstruction, nameof(ProofConstruction));
    public string StatementId { get; init; } = NormalizeRequiredValue(StatementId, nameof(StatementId));
    public string ProfileId { get; init; } = NormalizeRequiredValue(ProfileId, nameof(ProfileId));
    public string BallotDefinitionHash { get; init; } =
        NormalizeRequiredValue(BallotDefinitionHash, nameof(BallotDefinitionHash));
    public string BallotEncryptionSchemeVersion { get; init; } =
        NormalizeRequiredValue(BallotEncryptionSchemeVersion, nameof(BallotEncryptionSchemeVersion));
    public string ElectionPublicKeyId { get; init; } =
        NormalizeRequiredValue(ElectionPublicKeyId, nameof(ElectionPublicKeyId));
    public string AcceptedBallotSetHash { get; init; } =
        NormalizeRequiredValue(AcceptedBallotSetHash, nameof(AcceptedBallotSetHash));
    public string PublishedBallotStreamHash { get; init; } =
        NormalizeRequiredValue(PublishedBallotStreamHash, nameof(PublishedBallotStreamHash));
    public string ProofSystemVersion { get; init; } =
        NormalizeRequiredValue(ProofSystemVersion, nameof(ProofSystemVersion));
    public string ProofBytes { get; init; } = NormalizeRequiredValue(ProofBytes, nameof(ProofBytes));
    public string ProofHash { get; init; } = NormalizeRequiredValue(ProofHash, nameof(ProofHash));
    public string TranscriptHash { get; init; } =
        NormalizeRequiredValue(TranscriptHash, nameof(TranscriptHash));
    public string ExternalReviewStatus { get; init; } =
        NormalizeRequiredValue(ExternalReviewStatus, nameof(ExternalReviewStatus));
    public string? GeneratorReleaseHash { get; init; } = NormalizeOptionalValue(GeneratorReleaseHash);
    public string? VerifierReleaseHash { get; init; } = NormalizeOptionalValue(VerifierReleaseHash);
    public string? StatementHashSha512 { get; init; } = NormalizeOptionalValue(StatementHashSha512);
    public string? FiatShamirTranscriptHashSha512 { get; init; } =
        NormalizeOptionalValue(FiatShamirTranscriptHashSha512);
    public string? CanonicalProofBytesHex { get; init; } = NormalizeOptionalValue(CanonicalProofBytesHex);
    public string? CanonicalProofHashSha512 { get; init; } = NormalizeOptionalValue(CanonicalProofHashSha512);
    public int? CanonicalProofByteLength { get; init; } = CanonicalProofByteLength;

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        NormalizeStringList(PublicPrivacyBoundary);

    internal static string NormalizeRequiredValue(string? value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    internal static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static IReadOnlyList<string> NormalizeStringList(IReadOnlyList<string>? values) =>
        values is null
            ? Array.Empty<string>()
            : values
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToArray();
}

public record ElectionSp07PublicationProofManifestArtifactRecord(
    string Schema,
    string ElectionId,
    string ProofSessionId,
    string PlanId,
    string PublicationProofMode,
    string ProofConstruction,
    string StatementId,
    string ProfileId,
    string AcceptedBallotSetHash,
    string PublishedBallotStreamHash,
    int AcceptedBallotCount,
    int PublishedBallotCount,
    int CiphertextSlotCount,
    int ChunkCount,
    int CompletedChunkCount,
    int FailedChunkCount,
    double SlowestChunkMilliseconds,
    IReadOnlyList<ElectionSp07PublicationProofManifestChunkArtifactRecord> Chunks,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public const string SchemaVersion = "ElectionSp07PublicationProofManifest-v1";

    public string Schema { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(Schema, nameof(Schema));

    public string ElectionId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string ProofSessionId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            ProofSessionId,
            nameof(ProofSessionId));

    public string PlanId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(PlanId, nameof(PlanId));

    public string PublicationProofMode { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            PublicationProofMode,
            nameof(PublicationProofMode));

    public string ProofConstruction { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            ProofConstruction,
            nameof(ProofConstruction));

    public string StatementId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(StatementId, nameof(StatementId));

    public string ProfileId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ProfileId, nameof(ProfileId));

    public string AcceptedBallotSetHash { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            AcceptedBallotSetHash,
            nameof(AcceptedBallotSetHash));

    public string PublishedBallotStreamHash { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            PublishedBallotStreamHash,
            nameof(PublishedBallotStreamHash));

    public IReadOnlyList<ElectionSp07PublicationProofManifestChunkArtifactRecord> Chunks { get; init; } =
        Chunks?.ToArray() ?? Array.Empty<ElectionSp07PublicationProofManifestChunkArtifactRecord>();

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp07PublicationProofManifestChunkArtifactRecord(
    string ChunkId,
    int ChunkIndex,
    int Offset,
    int Count,
    bool Passed,
    string ResultCode,
    string ProofProfileId,
    string WorkerKind,
    string WorkerVersion,
    int WorkerThreadCount,
    string StatementHashSha512,
    string FiatShamirTranscriptHashSha512,
    string CanonicalProofHashSha512,
    int CanonicalProofByteLength,
    string? CanonicalProofBytesHex,
    string PublishedBallotStreamHash,
    double ElapsedMilliseconds,
    double? GenerationMilliseconds = null,
    double? SelfVerificationMilliseconds = null,
    double? CpuTimeMilliseconds = null,
    IReadOnlyList<string>? MemoryNotes = null)
{
    public string ChunkId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ChunkId, nameof(ChunkId));

    public string ResultCode { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ResultCode, nameof(ResultCode));

    public string ProofProfileId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            ProofProfileId,
            nameof(ProofProfileId));

    public string WorkerKind { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(WorkerKind, nameof(WorkerKind));

    public string WorkerVersion { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            WorkerVersion,
            nameof(WorkerVersion));

    public string StatementHashSha512 { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            StatementHashSha512,
            nameof(StatementHashSha512));

    public string FiatShamirTranscriptHashSha512 { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            FiatShamirTranscriptHashSha512,
            nameof(FiatShamirTranscriptHashSha512));

    public string CanonicalProofHashSha512 { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            CanonicalProofHashSha512,
            nameof(CanonicalProofHashSha512));

    public string? CanonicalProofBytesHex { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeOptionalValue(CanonicalProofBytesHex);

    public string PublishedBallotStreamHash { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            PublishedBallotStreamHash,
            nameof(PublishedBallotStreamHash));

    public IReadOnlyList<string> MemoryNotes { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeStringList(MemoryNotes);
}

public record ElectionSp07VerifierOutputArtifactRecord(
    string ElectionId,
    string VerifierProfileId,
    string StatementId,
    DateTime VerifiedAt,
    IReadOnlyList<VerifierCheckResultRecord> Results)
{
    public string ElectionId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string VerifierProfileId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            VerifierProfileId,
            nameof(VerifierProfileId));

    public string StatementId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(StatementId, nameof(StatementId));

    public IReadOnlyList<VerifierCheckResultRecord> Results { get; init; } =
        Results?.ToArray() ?? Array.Empty<VerifierCheckResultRecord>();
}

public record ElectionSp07WitnessDeletionReceiptArtifactRecord(
    string ElectionId,
    string WitnessSetHash,
    int WitnessCount,
    string TranscriptHash,
    string ProofHash,
    string DeletionStatus,
    DateTime DeletedAt,
    IReadOnlyList<string> PublicPrivacyBoundary)
{
    public string ElectionId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public string WitnessSetHash { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(WitnessSetHash, nameof(WitnessSetHash));

    public string TranscriptHash { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            TranscriptHash,
            nameof(TranscriptHash));

    public string ProofHash { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ProofHash, nameof(ProofHash));

    public string DeletionStatus { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(
            DeletionStatus,
            nameof(DeletionStatus));

    public IReadOnlyList<string> PublicPrivacyBoundary { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeStringList(PublicPrivacyBoundary);
}

public record ElectionSp07RestrictedProofSessionArtifactRecord(
    string ElectionId,
    IReadOnlyList<ElectionPublicationProofSessionRecord> Sessions)
{
    public string ElectionId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public IReadOnlyList<ElectionPublicationProofSessionRecord> Sessions { get; init; } =
        Sessions?.ToArray() ?? Array.Empty<ElectionPublicationProofSessionRecord>();
}

public record ElectionSp07RestrictedDeletionLogArtifactRecord(
    string ElectionId,
    IReadOnlyList<ElectionPublicationWitnessDeletionReceiptRecord> Receipts)
{
    public string ElectionId { get; init; } =
        ElectionSp07PublicationProofTranscriptArtifactRecord.NormalizeRequiredValue(ElectionId, nameof(ElectionId));

    public IReadOnlyList<ElectionPublicationWitnessDeletionReceiptRecord> Receipts { get; init; } =
        Receipts?.ToArray() ?? Array.Empty<ElectionPublicationWitnessDeletionReceiptRecord>();
}
