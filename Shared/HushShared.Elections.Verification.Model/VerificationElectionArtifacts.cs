namespace HushShared.Elections.Verification.Model;

public record AcceptedBallotSetArtifactRecord(
    string ElectionId,
    int AcceptedBallotCount,
    string AcceptedBallotInventoryHash,
    IReadOnlyList<AcceptedBallotArtifactRecord> AcceptedBallots);

public record AcceptedBallotArtifactRecord(
    string BallotNullifier,
    string EncryptedBallotPackage,
    string ProofBundle,
    string EncryptedBallotPackageHash,
    string ProofBundleHash,
    Guid? PreparedBallotId = null,
    string? PreparedBallotHash = null,
    string? ReceiptCommitment = null,
    string? ReceiptCommitmentScheme = null,
    int? BallotDefinitionVersion = null,
    byte[]? BallotDefinitionHash = null)
{
    public byte[]? BallotDefinitionHash { get; init; } = BallotDefinitionHash?.ToArray();
}

public record PublishedBallotStreamArtifactRecord(
    string ElectionId,
    int PublishedBallotCount,
    string PublishedBallotStreamHash,
    IReadOnlyList<PublishedBallotArtifactRecord> PublishedBallots);

public record PublishedBallotArtifactRecord(
    long PublicationSequence,
    string EncryptedBallotPackage,
    string ProofBundle,
    string EncryptedBallotPackageHash,
    string ProofBundleHash);

public record TallyReplayArtifactRecord(
    string ElectionId,
    string PublicationProofMode,
    VerificationCheckStatus EvidenceStatus,
    string ResultCode,
    string Message,
    string? AcceptedBallotSetHash = null,
    string? PublishedBallotStreamHash = null,
    string? PublicationProofTranscriptHash = null,
    string? PublicationProofHash = null,
    string? FinalEncryptedTallyHash = null);

public record TrusteeReleaseEvidenceArtifactRecord(
    string ElectionId,
    int FinalizationSessionCount,
    int AcceptedShareCount,
    IReadOnlyList<TrusteeReleaseShareEvidenceRecord> AcceptedShares);

public record TrusteeReleaseShareEvidenceRecord(
    string TrusteeUserAddress,
    int ShareIndex,
    string ShareMaterialHash,
    string Status);

public record ResultBindingArtifactRecord(
    string ElectionId,
    string ReportPackageId,
    string ReportPackageHash,
    string? FinalizeArtifactId,
    string? OfficialResultArtifactId,
    string? UnofficialResultArtifactId);

public record RestrictedRosterCheckoffArtifactRecord(
    string ElectionId,
    IReadOnlyList<RestrictedRosterCheckoffEntryRecord> Entries);

public record RestrictedRosterCheckoffEntryRecord(
    string OrganizationVoterId,
    string? LinkedActorPublicAddress,
    string VotingRightStatus,
    string? ParticipationStatus);

