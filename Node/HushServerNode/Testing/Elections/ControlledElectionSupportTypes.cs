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

internal sealed record ControlledElectionTrusteeShare(
    string ElectionId,
    string SessionId,
    string TargetTallyId,
    string TrusteeId,
    string ShareMaterial);

internal sealed record ControlledElectionReleaseAttempt(
    ControlledElectionThresholdDefinition ThresholdDefinition,
    string SessionId,
    string TargetTallyId,
    ImmutableArray<ControlledElectionTrusteeShare> SubmittedShares);
