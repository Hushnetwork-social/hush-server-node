namespace HushShared.Elections.PublicationProof;

public sealed record Sp07PointPayload(string X, string Y);

public sealed record Sp07CipherSlotPayload(Sp07PointPayload C1, Sp07PointPayload C2);

public sealed record Sp07CipherBallotPayload(IReadOnlyList<Sp07CipherSlotPayload> Slots);

public sealed record Sp07CommitmentKeyPayload(
    string Profile,
    int MatrixN,
    Sp07PointPayload H,
    IReadOnlyList<Sp07PointPayload> G);

public sealed record Sp07PublicationProofStatement(
    string ElectionId,
    string BallotDefinitionHash,
    string GroupProfile,
    IReadOnlyList<Sp07CipherBallotPayload> AcceptedBallots,
    IReadOnlyList<Sp07CipherBallotPayload> PublishedBallots,
    Sp07PointPayload ElectionPublicKey,
    Sp07CommitmentKeyPayload CommitmentKey,
    int MatrixM,
    int MatrixN);

public sealed record Sp07PublicationProofWitness(
    IReadOnlyList<int> PublishedToAccepted,
    IReadOnlyList<IReadOnlyList<string>> RerandomizationByPublishedBallotAndSlot);

public sealed record Sp07PublicationProofPayload(
    string Schema,
    string ProofSystemVersion,
    string MatrixProfile,
    IReadOnlyList<Sp07PointPayload> OuterCommitmentsA,
    IReadOnlyList<Sp07PointPayload> OuterCommitmentsB,
    Sp07ProductArgumentPayload ProductArgument,
    Sp07MultiExponentiationArgumentPayload MultiExponentiationArgument);

public sealed record Sp07ProductArgumentPayload(
    string Profile,
    Sp07SingleValueProductArgumentPayload SingleValueProduct);

public sealed record Sp07SingleValueProductArgumentPayload(
    Sp07PointPayload CommitmentD,
    Sp07PointPayload CommitmentDelta,
    Sp07PointPayload CommitmentCapitalDelta,
    IReadOnlyList<string> ATilde,
    IReadOnlyList<string> BTilde,
    string RTilde,
    string STilde);

public sealed record Sp07MultiExponentiationArgumentPayload(
    Sp07PointPayload CommitmentA0,
    IReadOnlyList<Sp07PointPayload> CommitmentB,
    IReadOnlyList<Sp07CipherBallotPayload> DiagonalCiphertexts,
    IReadOnlyList<string> A,
    string R,
    string B,
    string S,
    IReadOnlyList<string> TauBySlot);

public sealed record Sp07ProofGenerationResult(
    Sp07PublicationProofPayload Proof,
    string ProofBytes,
    string ProofHash,
    Sp07ProofVerificationResult SelfVerification);

public sealed record Sp07ProfiledProofGenerationResult(
    Sp07ProofGenerationResult Result,
    Sp07ProofGenerationProfile Profile);

public sealed record Sp07ProofGenerationProfile(
    IReadOnlyList<Sp07ProofTimingRecord> Timings);

public sealed record Sp07ProofTimingRecord(
    string Name,
    double ElapsedMilliseconds);

public sealed record Sp07ProofVerificationResult(
    bool IsValid,
    string ResultCode,
    string Message,
    string? ProofHash = null);
