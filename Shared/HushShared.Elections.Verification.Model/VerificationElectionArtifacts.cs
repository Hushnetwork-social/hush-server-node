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
    string ProofBundleHash);

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
    string Message);

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

