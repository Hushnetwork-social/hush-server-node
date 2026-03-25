using System.Collections.Immutable;
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

    public static string CreateTallyFingerprint(ControlledElectionTallyState tallyState)
    {
        ArgumentNullException.ThrowIfNull(tallyState);

        return string.Join(
            "|",
            tallyState.Slots.Select(slot =>
                $"{slot.C1.X}:{slot.C1.Y}:{slot.C2.X}:{slot.C2.Y}"));
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
}
