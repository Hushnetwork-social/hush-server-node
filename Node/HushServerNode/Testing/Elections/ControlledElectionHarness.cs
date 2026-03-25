using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using HushNode.Reactions.Crypto;
using HushShared.Reactions.Model;

namespace HushServerNode.Testing.Elections;

internal static class ControlledElectionHarness
{
    public const int DefaultSelectionCount = 6;

    public static ControlledElectionKeyPair CreateDeterministicKeyPair(BigInteger seed, IBabyJubJub? curve = null)
    {
        var activeCurve = curve ?? new BabyJubJubCurve();
        var privateKey = NormalizeScalar(seed, activeCurve);
        var publicKey = activeCurve.ScalarMul(activeCurve.Generator, privateKey);

        return new ControlledElectionKeyPair(privateKey, publicKey);
    }

    public static ImmutableArray<BigInteger> CreateDeterministicNonceSequence(
        BigInteger seed,
        int count,
        IBabyJubJub? curve = null)
    {
        var activeCurve = curve ?? new BabyJubJubCurve();
        if (count <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(count), count, "Nonce count must be positive.");
        }

        var start = NormalizeScalar(seed, activeCurve);

        return Enumerable.Range(0, count)
            .Select(index => NormalizeScalar(start + index + 1, activeCurve))
            .ToImmutableArray();
    }

    public static ControlledElectionThresholdSetup CreateControlledThresholdSetup(
        ControlledElectionThresholdDefinition thresholdDefinition,
        string sessionId,
        string targetTallyId,
        BigInteger seed,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(thresholdDefinition);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTallyId);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var thresholdValidation = ValidateThresholdDefinition(thresholdDefinition);
        if (!thresholdValidation.IsValid)
        {
            throw new InvalidOperationException(thresholdValidation.Notes);
        }

        var coefficients = CreateDeterministicCoefficients(seed, thresholdDefinition.Threshold, activeCurve);
        var publicKey = activeCurve.ScalarMul(activeCurve.Generator, coefficients[0]);
        var shares = thresholdDefinition.TrusteeIds
            .Select((trusteeId, index) => new ControlledElectionTrusteeShare(
                thresholdDefinition.ElectionId,
                sessionId,
                targetTallyId,
                trusteeId,
                index + 1,
                SerializeScalar(EvaluatePolynomial(coefficients, index + 1, activeCurve.Order))))
            .ToImmutableArray();

        return new ControlledElectionThresholdSetup(
            thresholdDefinition,
            sessionId,
            targetTallyId,
            publicKey,
            shares);
    }

    public static ControlledDkgCeremonyResult SimulateLocalDkgViability(
        ControlledElectionThresholdDefinition thresholdDefinition,
        string sessionId,
        string targetTallyId,
        BigInteger seed,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(thresholdDefinition);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetTallyId);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var thresholdValidation = ValidateThresholdDefinition(thresholdDefinition);
        if (!thresholdValidation.IsValid)
        {
            throw new InvalidOperationException(thresholdValidation.Notes);
        }

        var inboundShares = thresholdDefinition.TrusteeIds.ToDictionary(
            trusteeId => trusteeId,
            _ => ImmutableArray.CreateBuilder<BigInteger>(),
            StringComparer.Ordinal);
        var participantArtifacts = ImmutableArray.CreateBuilder<ControlledDkgParticipantArtifact>();
        var aggregatePublicKey = activeCurve.Identity;

        for (var participantIndex = 0; participantIndex < thresholdDefinition.TrusteeIds.Length; participantIndex++)
        {
            var trusteeId = thresholdDefinition.TrusteeIds[participantIndex];
            var participantSeed = seed + ((participantIndex + 1) * 1000);
            var coefficients = CreateDeterministicCoefficients(
                participantSeed,
                thresholdDefinition.Threshold,
                activeCurve);
            var publicCommitments = coefficients
                .Select(coefficient => activeCurve.ScalarMul(activeCurve.Generator, coefficient))
                .ToImmutableArray();
            var outboundSharePackages = thresholdDefinition.TrusteeIds
                .Select((recipientId, recipientIndex) =>
                {
                    var shareValue = EvaluatePolynomial(coefficients, recipientIndex + 1, activeCurve.Order);
                    inboundShares[recipientId].Add(shareValue);

                    return new ControlledDkgPeerSharePackage(
                        trusteeId,
                        recipientId,
                        recipientIndex + 1,
                        SerializeScalar(shareValue));
                })
                .ToImmutableArray();

            aggregatePublicKey = activeCurve.Add(aggregatePublicKey, publicCommitments[0]);
            participantArtifacts.Add(new ControlledDkgParticipantArtifact(
                trusteeId,
                publicCommitments,
                outboundSharePackages));
        }

        var finalShares = thresholdDefinition.TrusteeIds
            .Select((trusteeId, index) => new ControlledElectionTrusteeShare(
                thresholdDefinition.ElectionId,
                sessionId,
                targetTallyId,
                trusteeId,
                index + 1,
                SerializeScalar(inboundShares[trusteeId]
                    .Aggregate(BigInteger.Zero, (sum, current) => Mod(sum + current, activeCurve.Order)))))
            .ToImmutableArray();

        var thresholdSetup = new ControlledElectionThresholdSetup(
            thresholdDefinition,
            sessionId,
            targetTallyId,
            aggregatePublicKey,
            finalShares);

        return new ControlledDkgCeremonyResult(
            thresholdSetup,
            participantArtifacts.ToImmutable(),
            "Exploratory local multi-party DKG-style viability only. This does not prove a production ceremony.");
    }

    public static ControlledElectionBallot EncryptOneHotBallot(
        string ballotId,
        int choiceIndex,
        ECPoint publicKey,
        ImmutableArray<BigInteger> nonces,
        int selectionCount = DefaultSelectionCount,
        IBabyJubJub? curve = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ballotId);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var publicKeyValidation = ValidatePublicKey(publicKey, activeCurve);
        if (!publicKeyValidation.IsValid)
        {
            throw new InvalidOperationException(publicKeyValidation.Notes);
        }

        if (choiceIndex < 0 || choiceIndex >= selectionCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(choiceIndex),
                choiceIndex,
                $"Choice index must be between 0 and {selectionCount - 1}.");
        }

        var nonceValidation = ValidateNonceSequence(nonces, selectionCount, activeCurve);
        if (!nonceValidation.IsValid)
        {
            throw new InvalidOperationException(nonceValidation.Notes);
        }

        var slots = Enumerable.Range(0, selectionCount)
            .Select(index =>
            {
                var message = index == choiceIndex ? BigInteger.One : BigInteger.Zero;
                return EncryptSelection(message, publicKey, nonces[index], activeCurve);
            })
            .ToImmutableArray();

        return new ControlledElectionBallot(ballotId, slots);
    }

    public static ControlledElectionBallot RerandomizeBallot(
        ControlledElectionBallot ballot,
        ECPoint publicKey,
        ImmutableArray<BigInteger> nonces,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(ballot);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var publicKeyValidation = ValidatePublicKey(publicKey, activeCurve);
        if (!publicKeyValidation.IsValid)
        {
            throw new InvalidOperationException(publicKeyValidation.Notes);
        }

        var ballotValidation = ValidateBallot(ballot, ballot.Slots.Length, activeCurve);
        if (!ballotValidation.IsValid)
        {
            throw new InvalidOperationException(ballotValidation.Notes);
        }

        var nonceValidation = ValidateNonceSequence(nonces, ballot.Slots.Length, activeCurve);
        if (!nonceValidation.IsValid)
        {
            throw new InvalidOperationException(nonceValidation.Notes);
        }

        var rerandomizedSlots = ballot.Slots
            .Select((slot, index) =>
            {
                var zeroEncryption = EncryptSelection(BigInteger.Zero, publicKey, nonces[index], activeCurve);
                return new ControlledEncryptedSelection(
                    activeCurve.Add(slot.C1, zeroEncryption.C1),
                    activeCurve.Add(slot.C2, zeroEncryption.C2));
            })
            .ToImmutableArray();

        return ballot with { Slots = rerandomizedSlots };
    }

    public static ControlledElectionTallyState CreateEmptyTallyState(
        string electionId,
        int selectionCount = DefaultSelectionCount,
        IBabyJubJub? curve = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(electionId);

        var activeCurve = curve ?? new BabyJubJubCurve();
        if (selectionCount <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(selectionCount),
                selectionCount,
                "Selection count must be positive.");
        }

        var slots = Enumerable.Range(0, selectionCount)
            .Select(_ => new ControlledEncryptedSelection(activeCurve.Identity, activeCurve.Identity))
            .ToImmutableArray();

        return new ControlledElectionTallyState(electionId, slots);
    }

    public static ControlledElectionTallyState CreateProtectedTallyFromCounts(
        string electionId,
        ImmutableArray<BigInteger> counts,
        ECPoint publicKey,
        ImmutableArray<BigInteger> nonces,
        IBabyJubJub? curve = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(electionId);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var publicKeyValidation = ValidatePublicKey(publicKey, activeCurve);
        if (!publicKeyValidation.IsValid)
        {
            throw new InvalidOperationException(publicKeyValidation.Notes);
        }

        if (counts.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("Controlled tally counts must contain at least one slot.");
        }

        if (counts.Any(count => count < BigInteger.Zero))
        {
            throw new InvalidOperationException("Controlled tally counts cannot contain negative values.");
        }

        var nonceValidation = ValidateNonceSequence(nonces, counts.Length, activeCurve);
        if (!nonceValidation.IsValid)
        {
            throw new InvalidOperationException(nonceValidation.Notes);
        }

        var slots = counts
            .Select((count, index) => EncryptSelection(count, publicKey, nonces[index], activeCurve))
            .ToImmutableArray();

        return new ControlledElectionTallyState(electionId, slots);
    }

    public static ControlledElectionTallyState AccumulateBallot(
        ControlledElectionTallyState tallyState,
        ControlledElectionBallot ballot,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(tallyState);
        ArgumentNullException.ThrowIfNull(ballot);

        var activeCurve = curve ?? new BabyJubJubCurve();
        if (tallyState.Slots.Length != ballot.Slots.Length)
        {
            throw new InvalidOperationException("Ballot slot count must match tally slot count.");
        }

        var updatedSlots = tallyState.Slots
            .Select((slot, index) => new ControlledEncryptedSelection(
                activeCurve.Add(slot.C1, ballot.Slots[index].C1),
                activeCurve.Add(slot.C2, ballot.Slots[index].C2)))
            .ToImmutableArray();

        return tallyState with { Slots = updatedSlots };
    }

    public static ControlledElectionTallyState AccumulateBallots(
        string electionId,
        ImmutableArray<ControlledElectionBallot> ballots,
        IBabyJubJub? curve = null)
    {
        if (ballots.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("At least one ballot is required to accumulate a tally.");
        }

        var activeCurve = curve ?? new BabyJubJubCurve();
        var tally = CreateEmptyTallyState(electionId, ballots[0].Slots.Length, activeCurve);

        return ballots.Aggregate(tally, (current, ballot) => AccumulateBallot(current, ballot, activeCurve));
    }

    public static ControlledElectionReleaseResult TryReleaseProtectedTally(
        ControlledElectionThresholdSetup thresholdSetup,
        ControlledElectionReleaseAttempt releaseAttempt,
        ControlledElectionTallyState tallyState,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(thresholdSetup);
        ArgumentNullException.ThrowIfNull(releaseAttempt);
        ArgumentNullException.ThrowIfNull(tallyState);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var thresholdValidation = ValidateThresholdDefinition(thresholdSetup.ThresholdDefinition);
        if (!thresholdValidation.IsValid)
        {
            return ControlledElectionReleaseResult.Failure(
                "INVALID_THRESHOLD_CONFIGURATION",
                thresholdValidation.Notes);
        }

        if (tallyState.ElectionId != thresholdSetup.ThresholdDefinition.ElectionId)
        {
            return ControlledElectionReleaseResult.Failure(
                "WRONG_TARGET_SHARE",
                "Controlled tally election identifier does not match the threshold setup.");
        }

        if (!ThresholdDefinitionsMatch(thresholdSetup.ThresholdDefinition, releaseAttempt.ThresholdDefinition) ||
            !string.Equals(thresholdSetup.SessionId, releaseAttempt.SessionId, StringComparison.Ordinal) ||
            !string.Equals(thresholdSetup.TargetTallyId, releaseAttempt.TargetTallyId, StringComparison.Ordinal))
        {
            return ControlledElectionReleaseResult.Failure(
                "WRONG_TARGET_SHARE",
                "Controlled release attempt does not match the configured threshold session or tally target.");
        }

        if (releaseAttempt.SubmittedShares.IsDefaultOrEmpty)
        {
            return ControlledElectionReleaseResult.Failure(
                "INSUFFICIENT_SHARES",
                "Controlled release attempt must include at least one trustee share.");
        }

        var duplicateTrustees = releaseAttempt.SubmittedShares
            .GroupBy(share => share.TrusteeId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateTrustees.Length > 0)
        {
            return ControlledElectionReleaseResult.Failure(
                "DUPLICATE_SHARE",
                $"Controlled release attempt contains duplicate trustee shares: {string.Join(", ", duplicateTrustees)}.");
        }

        var duplicateIndices = releaseAttempt.SubmittedShares
            .GroupBy(share => share.ShareIndex)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIndices.Length > 0)
        {
            return ControlledElectionReleaseResult.Failure(
                "DUPLICATE_SHARE",
                $"Controlled release attempt contains duplicate share indexes: {string.Join(", ", duplicateIndices)}.");
        }

        if (releaseAttempt.SubmittedShares.Length < thresholdSetup.ThresholdDefinition.Threshold)
        {
            return ControlledElectionReleaseResult.Failure(
                "INSUFFICIENT_SHARES",
                $"Controlled release attempt provided {releaseAttempt.SubmittedShares.Length} shares but requires {thresholdSetup.ThresholdDefinition.Threshold}.");
        }

        var canonicalShares = thresholdSetup.Shares.ToDictionary(
            share => share.TrusteeId,
            StringComparer.Ordinal);

        foreach (var share in releaseAttempt.SubmittedShares)
        {
            if (!string.Equals(share.ElectionId, thresholdSetup.ThresholdDefinition.ElectionId, StringComparison.Ordinal) ||
                !string.Equals(share.SessionId, thresholdSetup.SessionId, StringComparison.Ordinal) ||
                !string.Equals(share.TargetTallyId, thresholdSetup.TargetTallyId, StringComparison.Ordinal))
            {
                return ControlledElectionReleaseResult.Failure(
                    "WRONG_TARGET_SHARE",
                    $"Controlled share '{share.TrusteeId}' is bound to a different election/session/target.");
            }

            if (!canonicalShares.TryGetValue(share.TrusteeId, out var expectedShare))
            {
                return ControlledElectionReleaseResult.Failure(
                    "MALFORMED_SHARE",
                    $"Controlled share '{share.TrusteeId}' is not part of the configured threshold setup.");
            }

            if (share.ShareIndex != expectedShare.ShareIndex ||
                !string.Equals(share.ShareMaterial, expectedShare.ShareMaterial, StringComparison.Ordinal))
            {
                return ControlledElectionReleaseResult.Failure(
                    "MALFORMED_SHARE",
                    $"Controlled share '{share.TrusteeId}' does not match the configured share material.");
            }
        }

        BigInteger reconstructedSecret;
        try
        {
            reconstructedSecret = ReconstructSecretScalarFromShares(releaseAttempt.SubmittedShares, activeCurve);
        }
        catch (Exception ex)
        {
            return ControlledElectionReleaseResult.Failure(
                "MALFORMED_SHARE",
                $"Controlled release failed during share reconstruction: {ex.Message}");
        }

        var derivedPublicKey = activeCurve.ScalarMul(activeCurve.Generator, reconstructedSecret);
        if (derivedPublicKey != thresholdSetup.PublicKey)
        {
            return ControlledElectionReleaseResult.Failure(
                "MALFORMED_SHARE",
                "Controlled release shares reconstruct a different public key than the configured threshold setup.");
        }

        var releasedSelections = tallyState.Slots
            .Select(slot => activeCurve.Subtract(slot.C2, activeCurve.ScalarMul(slot.C1, reconstructedSecret)))
            .ToImmutableArray();

        return ControlledElectionReleaseResult.Success(
            releasedSelections,
            $"Controlled tally release succeeded with {releaseAttempt.SubmittedShares.Length} share(s).");
    }

    public static ControlledElectionReleaseResult TryReleaseProtectedBallot(
        ControlledElectionThresholdSetup thresholdSetup,
        ControlledElectionReleaseAttempt releaseAttempt,
        ControlledElectionBallot ballot,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(thresholdSetup);
        ArgumentNullException.ThrowIfNull(releaseAttempt);
        ArgumentNullException.ThrowIfNull(ballot);
        _ = curve;

        return ControlledElectionReleaseResult.Failure(
            "SINGLE_BALLOT_RELEASE_FORBIDDEN",
            "Controlled release harness permits only aggregate tally release. Single-ballot decrypt paths are intentionally refused.");
    }

    public static ControlledElectionValidationResult ValidateAggregateOnlyCountingPath(
        bool requiresIndividualBallotDecryption)
    {
        return requiresIndividualBallotDecryption
            ? ControlledElectionValidationResult.Failure(
                "SINGLE_BALLOT_DECRYPTION_FORBIDDEN",
                "Controlled tallying paths that require individual ballot decryption are rejected.")
            : ControlledElectionValidationResult.Success(
                "Controlled tallying path stays aggregate-only.");
    }

    public static ControlledElectionDecodeResult TryDecryptBallotForHarness(
        ControlledElectionBallot ballot,
        BigInteger privateKey,
        BigInteger maxSupportedCount,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(ballot);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var ballotValidation = ValidateBallot(ballot, ballot.Slots.Length, activeCurve);
        if (!ballotValidation.IsValid)
        {
            return ControlledElectionDecodeResult.Failure(
                ballotValidation.FailureCode ?? "INVALID_CIPHERTEXT_STRUCTURE",
                maxSupportedCount,
                ballotValidation.Notes);
        }

        var releasedSelections = ballot.Slots
            .Select(slot => activeCurve.Subtract(slot.C2, activeCurve.ScalarMul(slot.C1, privateKey)))
            .ToImmutableArray();

        return TryDecodeReleasedSelections(releasedSelections, maxSupportedCount, activeCurve);
    }

    public static ControlledElectionDecodeResult TryDecryptTallyForHarness(
        ControlledElectionTallyState tallyState,
        BigInteger privateKey,
        BigInteger maxSupportedCount,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(tallyState);

        var activeCurve = curve ?? new BabyJubJubCurve();
        var releasedSelections = tallyState.Slots
            .Select(slot => activeCurve.Subtract(slot.C2, activeCurve.ScalarMul(slot.C1, privateKey)))
            .ToImmutableArray();

        return TryDecodeReleasedSelections(releasedSelections, maxSupportedCount, activeCurve);
    }

    public static ControlledElectionDecodeResult TryDecodeReleasedSelections(
        ImmutableArray<ECPoint> releasedSelections,
        BigInteger maxSupportedCount,
        IBabyJubJub? curve = null)
    {
        var activeCurve = curve ?? new BabyJubJubCurve();
        if (releasedSelections.IsDefaultOrEmpty)
        {
            return ControlledElectionDecodeResult.Failure(
                "EMPTY_RELEASED_SELECTIONS",
                maxSupportedCount,
                "Controlled decode requires at least one released selection.");
        }

        if (maxSupportedCount < BigInteger.Zero)
        {
            return ControlledElectionDecodeResult.Failure(
                "INVALID_DECODE_BOUND",
                maxSupportedCount,
                "Controlled decode bound must be zero or greater.");
        }

        var decodedCounts = ImmutableArray.CreateBuilder<BigInteger>(releasedSelections.Length);

        foreach (var releasedSelection in releasedSelections)
        {
            if (!activeCurve.IsOnCurve(releasedSelection))
            {
                return ControlledElectionDecodeResult.Failure(
                    "INVALID_RELEASED_SELECTION",
                    maxSupportedCount,
                    "Controlled decode received a released selection that is not on the Baby JubJub curve.");
            }

            var decodedCount = TryDecodePointToCount(releasedSelection, maxSupportedCount, activeCurve);
            if (decodedCount is null)
            {
                return ControlledElectionDecodeResult.Failure(
                    "DECODE_BOUND_EXCEEDED",
                    maxSupportedCount,
                    $"Controlled decode could not resolve a released selection within bound '{maxSupportedCount}'.");
            }

            decodedCounts.Add(decodedCount.Value);
        }

        return ControlledElectionDecodeResult.Success(
            maxSupportedCount,
            decodedCounts.ToImmutable(),
            $"Controlled decode succeeded within bound '{maxSupportedCount}'.");
    }

    public static ControlledElectionValidationResult ValidatePublicKey(ECPoint publicKey, IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(publicKey);

        var activeCurve = curve ?? new BabyJubJubCurve();
        if (!activeCurve.IsOnCurve(publicKey))
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_PUBLIC_KEY",
                "Controlled election public key must be a point on the Baby JubJub curve.");
        }

        if (publicKey == activeCurve.Identity)
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_PUBLIC_KEY",
                "Controlled election public key cannot be the identity point.");
        }

        return ControlledElectionValidationResult.Success("Controlled election public key is structurally valid.");
    }

    public static ControlledElectionValidationResult ValidateBallot(
        ControlledElectionBallot ballot,
        int? expectedSelectionCount = null,
        IBabyJubJub? curve = null)
    {
        ArgumentNullException.ThrowIfNull(ballot);

        var activeCurve = curve ?? new BabyJubJubCurve();
        if (string.IsNullOrWhiteSpace(ballot.BallotId))
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_CIPHERTEXT_STRUCTURE",
                "Controlled ballot must have a ballot identifier.");
        }

        if (ballot.Slots.IsDefaultOrEmpty)
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_CIPHERTEXT_STRUCTURE",
                "Controlled ballot must contain at least one encrypted slot.");
        }

        if (expectedSelectionCount.HasValue && ballot.Slots.Length != expectedSelectionCount.Value)
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_CIPHERTEXT_STRUCTURE",
                $"Controlled ballot slot count '{ballot.Slots.Length}' does not match expected '{expectedSelectionCount.Value}'.");
        }

        foreach (var slot in ballot.Slots)
        {
            if (!activeCurve.IsOnCurve(slot.C1) || !activeCurve.IsOnCurve(slot.C2))
            {
                return ControlledElectionValidationResult.Failure(
                    "INVALID_CIPHERTEXT_STRUCTURE",
                    "Controlled ballot contains a ciphertext point that is not on the Baby JubJub curve.");
            }
        }

        return ControlledElectionValidationResult.Success("Controlled ballot structure is valid.");
    }

    public static ControlledElectionValidationResult ValidateThresholdDefinition(
        ControlledElectionThresholdDefinition thresholdDefinition)
    {
        ArgumentNullException.ThrowIfNull(thresholdDefinition);

        if (string.IsNullOrWhiteSpace(thresholdDefinition.ElectionId))
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_THRESHOLD_CONFIGURATION",
                "Threshold definition must include an election identifier.");
        }

        if (thresholdDefinition.TrusteeIds.IsDefaultOrEmpty)
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_THRESHOLD_CONFIGURATION",
                "Threshold definition must include at least one trustee.");
        }

        if (thresholdDefinition.Threshold <= 0)
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_THRESHOLD_CONFIGURATION",
                "Threshold must be greater than zero.");
        }

        if (thresholdDefinition.Threshold > thresholdDefinition.TrusteeIds.Length)
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_THRESHOLD_CONFIGURATION",
                "Threshold cannot exceed the number of trustees.");
        }

        var duplicates = thresholdDefinition.TrusteeIds
            .GroupBy(trusteeId => trusteeId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_THRESHOLD_CONFIGURATION",
                $"Threshold definition contains duplicate trustee identifiers: {string.Join(", ", duplicates)}.");
        }

        if (thresholdDefinition.TrusteeIds.Any(string.IsNullOrWhiteSpace))
        {
            return ControlledElectionValidationResult.Failure(
                "INVALID_THRESHOLD_CONFIGURATION",
                "Threshold definition trustee identifiers must be non-empty.");
        }

        return ControlledElectionValidationResult.Success("Threshold definition is structurally valid.");
    }

    public static ControlledElectionValidationResult ValidateNonceSequence(
        ImmutableArray<BigInteger> nonces,
        int expectedCount,
        IBabyJubJub? curve = null)
    {
        var activeCurve = curve ?? new BabyJubJubCurve();
        if (nonces.IsDefaultOrEmpty)
        {
            return ControlledElectionValidationResult.Failure(
                "UNSAFE_NONCE",
                "Controlled nonce sequence must contain at least one nonce.");
        }

        if (nonces.Length != expectedCount)
        {
            return ControlledElectionValidationResult.Failure(
                "UNSAFE_NONCE",
                $"Controlled nonce sequence length '{nonces.Length}' does not match expected '{expectedCount}'.");
        }

        var duplicateNonces = nonces
            .GroupBy(nonce => nonce)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicateNonces.Length > 0)
        {
            return ControlledElectionValidationResult.Failure(
                "UNSAFE_NONCE",
                $"Controlled nonce sequence contains duplicate values: {string.Join(", ", duplicateNonces)}.");
        }

        if (nonces.Any(nonce => nonce <= BigInteger.Zero || nonce >= activeCurve.Order))
        {
            return ControlledElectionValidationResult.Failure(
                "UNSAFE_NONCE",
                "Controlled nonce sequence must contain only values greater than zero and below the subgroup order.");
        }

        return ControlledElectionValidationResult.Success("Controlled nonce sequence is structurally safe.");
    }

    public static BigInteger ReconstructSecretScalarFromShares(
        ImmutableArray<ControlledElectionTrusteeShare> shares,
        IBabyJubJub? curve = null)
    {
        var activeCurve = curve ?? new BabyJubJubCurve();
        if (shares.IsDefaultOrEmpty)
        {
            throw new InvalidOperationException("At least one share is required to reconstruct the controlled secret scalar.");
        }

        var duplicateTrustees = shares
            .GroupBy(share => share.TrusteeId, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateTrustees.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate trustee shares cannot reconstruct the controlled secret scalar: {string.Join(", ", duplicateTrustees)}.");
        }

        var duplicateIndices = shares
            .GroupBy(share => share.ShareIndex)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();
        if (duplicateIndices.Length > 0)
        {
            throw new InvalidOperationException(
                $"Duplicate share indexes cannot reconstruct the controlled secret scalar: {string.Join(", ", duplicateIndices)}.");
        }

        var secret = BigInteger.Zero;
        foreach (var share in shares)
        {
            var xCoordinate = new BigInteger(share.ShareIndex);
            var yCoordinate = ParseScalar(share.ShareMaterial);
            var numerator = BigInteger.One;
            var denominator = BigInteger.One;

            foreach (var otherShare in shares)
            {
                if (ReferenceEquals(share, otherShare))
                {
                    continue;
                }

                var otherX = new BigInteger(otherShare.ShareIndex);
                numerator = Mod(numerator * (-otherX), activeCurve.Order);
                denominator = Mod(denominator * (xCoordinate - otherX), activeCurve.Order);
            }

            var lagrangeCoefficient = Mod(
                numerator * ModInverse(denominator, activeCurve.Order),
                activeCurve.Order);
            secret = Mod(secret + (yCoordinate * lagrangeCoefficient), activeCurve.Order);
        }

        return secret;
    }

    public static string CreateTallyFingerprint(ControlledElectionTallyState tallyState)
    {
        ArgumentNullException.ThrowIfNull(tallyState);

        return string.Join(
            "|",
            tallyState.Slots.Select(slot =>
                $"{slot.C1.X}:{slot.C1.Y}:{slot.C2.X}:{slot.C2.Y}"));
    }

    private static bool ThresholdDefinitionsMatch(
        ControlledElectionThresholdDefinition left,
        ControlledElectionThresholdDefinition right) =>
        string.Equals(left.ElectionId, right.ElectionId, StringComparison.Ordinal) &&
        left.Threshold == right.Threshold &&
        left.TrusteeIds.SequenceEqual(right.TrusteeIds, StringComparer.Ordinal);

    private static ImmutableArray<BigInteger> CreateDeterministicCoefficients(
        BigInteger seed,
        int count,
        IBabyJubJub curve) =>
        Enumerable.Range(0, count)
            .Select(index => NormalizeScalar(seed + ((index + 1) * 7919), curve))
            .ToImmutableArray();

    private static BigInteger EvaluatePolynomial(
        ImmutableArray<BigInteger> coefficients,
        int x,
        BigInteger modulus)
    {
        var result = BigInteger.Zero;
        var power = BigInteger.One;

        foreach (var coefficient in coefficients)
        {
            result = Mod(result + (coefficient * power), modulus);
            power = Mod(power * x, modulus);
        }

        return result;
    }

    private static BigInteger NormalizeScalar(BigInteger seed, IBabyJubJub curve)
    {
        var normalized = seed % (curve.Order - 1);
        if (normalized < 0)
        {
            normalized += curve.Order - 1;
        }

        return normalized + 1;
    }

    private static string SerializeScalar(BigInteger scalar) =>
        scalar.ToString(CultureInfo.InvariantCulture);

    private static BigInteger ParseScalar(string scalar) =>
        BigInteger.Parse(scalar, CultureInfo.InvariantCulture);

    private static BigInteger Mod(BigInteger value, BigInteger modulus)
    {
        var normalized = value % modulus;
        return normalized < 0 ? normalized + modulus : normalized;
    }

    private static BigInteger ModInverse(BigInteger value, BigInteger modulus)
    {
        var normalizedValue = Mod(value, modulus);
        if (normalizedValue == BigInteger.Zero)
        {
            throw new InvalidOperationException("Cannot invert zero in the controlled threshold harness.");
        }

        var t = BigInteger.Zero;
        var newT = BigInteger.One;
        var r = modulus;
        var newR = normalizedValue;

        while (newR != BigInteger.Zero)
        {
            var quotient = r / newR;
            (t, newT) = (newT, t - (quotient * newT));
            (r, newR) = (newR, r - (quotient * newR));
        }

        if (r > BigInteger.One)
        {
            throw new InvalidOperationException("Controlled threshold share denominator is not invertible.");
        }

        return t < 0 ? t + modulus : t;
    }

    private static ControlledEncryptedSelection EncryptSelection(
        BigInteger message,
        ECPoint publicKey,
        BigInteger nonce,
        IBabyJubJub curve)
    {
        var c1 = curve.ScalarMul(curve.Generator, nonce);
        var messagePoint = message == BigInteger.Zero
            ? curve.Identity
            : curve.ScalarMul(curve.Generator, message);
        var sharedSecret = curve.ScalarMul(publicKey, nonce);
        var c2 = curve.Add(messagePoint, sharedSecret);

        return new ControlledEncryptedSelection(c1, c2);
    }

    private static BigInteger? TryDecodePointToCount(
        ECPoint releasedSelection,
        BigInteger maxSupportedCount,
        IBabyJubJub curve)
    {
        var current = curve.Identity;
        if (releasedSelection == current)
        {
            return BigInteger.Zero;
        }

        for (var count = BigInteger.One; count <= maxSupportedCount; count++)
        {
            current = curve.Add(current, curve.Generator);
            if (releasedSelection == current)
            {
                return count;
            }
        }

        return null;
    }
}
