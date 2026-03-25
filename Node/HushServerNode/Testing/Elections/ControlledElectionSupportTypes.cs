using System.Collections.Immutable;
using System.Numerics;
using HushShared.Reactions.Model;

namespace HushServerNode.Testing.Elections;

internal sealed record ControlledElectionKeyPair(
    BigInteger PrivateKey,
    ECPoint PublicKey);

internal sealed record ControlledEncryptedSelection(
    ECPoint C1,
    ECPoint C2);

internal sealed record ControlledElectionBallot(
    string BallotId,
    ImmutableArray<ControlledEncryptedSelection> Slots);

internal sealed record ControlledElectionTallyState(
    string ElectionId,
    ImmutableArray<ControlledEncryptedSelection> Slots);

internal sealed record ControlledElectionThresholdDefinition(
    string ElectionId,
    ImmutableArray<string> TrusteeIds,
    int Threshold);

internal sealed record ControlledElectionThresholdSetup(
    ControlledElectionThresholdDefinition ThresholdDefinition,
    string SessionId,
    string TargetTallyId,
    ECPoint PublicKey,
    ImmutableArray<ControlledElectionTrusteeShare> Shares);

internal sealed record ControlledElectionTrusteeShare(
    string ElectionId,
    string SessionId,
    string TargetTallyId,
    string TrusteeId,
    int ShareIndex,
    string ShareMaterial);

internal sealed record ControlledElectionReleaseAttempt(
    ControlledElectionThresholdDefinition ThresholdDefinition,
    string SessionId,
    string TargetTallyId,
    ImmutableArray<ControlledElectionTrusteeShare> SubmittedShares);

internal sealed record ControlledElectionReleaseResult(
    bool IsSuccessful,
    string? FailureCode,
    string Notes,
    ImmutableArray<ECPoint> ReleasedSelections)
{
    public static ControlledElectionReleaseResult Success(
        ImmutableArray<ECPoint> releasedSelections,
        string notes) =>
        new(true, null, notes, releasedSelections);

    public static ControlledElectionReleaseResult Failure(
        string failureCode,
        string notes) =>
        new(false, failureCode, notes, ImmutableArray<ECPoint>.Empty);
}

internal sealed record ControlledElectionDecodeResult(
    bool IsSuccessful,
    string? FailureCode,
    string Notes,
    BigInteger SupportedUpperBound,
    ImmutableArray<BigInteger> DecodedCounts)
{
    public static ControlledElectionDecodeResult Success(
        BigInteger supportedUpperBound,
        ImmutableArray<BigInteger> decodedCounts,
        string notes) =>
        new(true, null, notes, supportedUpperBound, decodedCounts);

    public static ControlledElectionDecodeResult Failure(
        string failureCode,
        BigInteger supportedUpperBound,
        string notes) =>
        new(false, failureCode, notes, supportedUpperBound, ImmutableArray<BigInteger>.Empty);
}

internal sealed record ControlledDkgPeerSharePackage(
    string FromTrusteeId,
    string ToTrusteeId,
    int ShareIndex,
    string ShareMaterial);

internal sealed record ControlledDkgParticipantArtifact(
    string TrusteeId,
    ImmutableArray<ECPoint> PublicCommitments,
    ImmutableArray<ControlledDkgPeerSharePackage> OutboundSharePackages);

internal sealed record ControlledDkgCeremonyResult(
    ControlledElectionThresholdSetup ThresholdSetup,
    ImmutableArray<ControlledDkgParticipantArtifact> ParticipantArtifacts,
    string Notes);

internal sealed record ControlledElectionArtifactInspectionResult(
    bool IsClean,
    string Notes,
    ImmutableArray<string> Findings)
{
    public static ControlledElectionArtifactInspectionResult Clean(string notes) =>
        new(true, notes, ImmutableArray<string>.Empty);

    public static ControlledElectionArtifactInspectionResult Dirty(
        string notes,
        ImmutableArray<string> findings) =>
        new(false, notes, findings);
}

internal static class ControlledElectionDecodeTiers
{
    public static readonly BigInteger DevSmoke = new(64);
    public static readonly BigInteger ClubRollout = new(5000);
    public static readonly BigInteger UpperSupported = new(20000);
}
