using System.Collections.Immutable;
using System.Globalization;
using System.Numerics;
using System.Text.Json;
using HushServerNode.Testing.Elections;
using HushShared.Reactions.Model;

namespace HushNode.IntegrationTests.Infrastructure;

internal sealed class ElectionCryptoFixtureLoader
{
    public const string SupportedFixtureVersion = "feat-107.v1";
    public const string DeprecatedFixtureVersion = "feat-107.v0";
    public const string VulnerableFixtureVersion = "feat-107.v0-broken";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<LoadedControlledElectionFixturePack> LoadAsync(string fixturePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixturePath);

        await using var stream = File.OpenRead(fixturePath);
        var payload = await JsonSerializer.DeserializeAsync<ControlledElectionFixturePackPayload>(stream, JsonOptions);
        if (payload is null)
        {
            throw new InvalidOperationException("Unable to deserialize the controlled FEAT-107 fixture payload.");
        }

        return new LoadedControlledElectionFixturePack(
            payload.FixtureVersion ?? throw new InvalidOperationException("Fixture version is required."),
            payload.Profile ?? throw new InvalidOperationException("Fixture profile is required."),
            payload.DecodeTier ?? throw new InvalidOperationException("Fixture decode tier is required."),
            ParseBigInteger(payload.DecodeBound, "decodeBound"),
            payload.CircuitVersion ?? throw new InvalidOperationException("Fixture circuit version is required."),
            payload.Deterministic,
            payload.GeneratedAt ?? throw new InvalidOperationException("Fixture generatedAt is required."),
            ParsePoint(payload.PublicKey, "publicKey"),
            ParseBallot(payload.Ballot, "client-ballot"),
            ParseBallot(payload.RerandomizedBallot, "client-rerandomized-ballot"),
            ParseBigIntegerArray(payload.ExpectedAggregateTally, "expectedAggregateTally"),
            ParseTestOnlyMaterial(payload.TestOnly));
    }

    public ElectionCryptoFixtureVersionValidation EvaluateVersionPolicy(string fixtureVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fixtureVersion);

        return fixtureVersion switch
        {
            SupportedFixtureVersion => ElectionCryptoFixtureVersionValidation.Accepted(
                "supported",
                $"Fixture version '{fixtureVersion}' is supported by the current FEAT-107 interoperability policy."),
            DeprecatedFixtureVersion => ElectionCryptoFixtureVersionValidation.Accepted(
                "deprecated",
                $"Fixture version '{fixtureVersion}' is deprecated but still allowed for FEAT-107 interoperability smoke."),
            VulnerableFixtureVersion => ElectionCryptoFixtureVersionValidation.Rejected(
                "vulnerable",
                $"Fixture version '{fixtureVersion}' is marked vulnerable and must be rejected immediately."),
            _ => ElectionCryptoFixtureVersionValidation.Rejected(
                "unknown",
                $"Fixture version '{fixtureVersion}' is outside the FEAT-107 interoperability policy window."),
        };
    }

    private static LoadedControlledElectionBallot ParseBallot(
        ControlledElectionBallotPayload? payload,
        string ballotId)
    {
        if (payload is null)
        {
            throw new InvalidOperationException($"Fixture ballot payload '{ballotId}' is required.");
        }

        var c1 = payload.Ciphertext?.C1 ?? throw new InvalidOperationException($"Fixture ballot '{ballotId}' is missing ciphertext.c1.");
        var c2 = payload.Ciphertext?.C2 ?? throw new InvalidOperationException($"Fixture ballot '{ballotId}' is missing ciphertext.c2.");
        if (c1.Length != c2.Length)
        {
            throw new InvalidOperationException($"Fixture ballot '{ballotId}' must have matching c1/c2 lengths.");
        }

        var slots = c1
            .Select((point, index) => new ControlledEncryptedSelection(
                ParsePoint(point, $"{ballotId}.c1[{index}]"),
                ParsePoint(c2[index], $"{ballotId}.c2[{index}]")))
            .ToImmutableArray();

        return new LoadedControlledElectionBallot(
            payload.ChoiceIndex,
            payload.SelectionCount,
            new ControlledElectionBallot(ballotId, slots),
            ParseBigIntegerArray(payload.Nonces, $"{ballotId}.nonces"),
            ParseBigIntegerArray(payload.ExpectedPlaintextSlots, $"{ballotId}.expectedPlaintextSlots"));
    }

    private static LoadedControlledElectionTestOnlyMaterial ParseTestOnlyMaterial(
        ControlledElectionTestOnlyPayload? payload)
    {
        if (payload is null)
        {
            throw new InvalidOperationException("Fixture testOnly payload is required.");
        }

        return new LoadedControlledElectionTestOnlyMaterial(
            ParseBigInteger(payload.Seed, "testOnly.seed"),
            ParseBigInteger(payload.PrivateKey, "testOnly.privateKey"),
            ParseBigInteger(payload.EncryptionNonceSeed, "testOnly.encryptionNonceSeed"),
            ParseBigInteger(payload.RerandomizationNonceSeed, "testOnly.rerandomizationNonceSeed"));
    }

    private static ECPoint ParsePoint(ControlledElectionPointPayload? payload, string label)
    {
        if (payload is null)
        {
            throw new InvalidOperationException($"Fixture point '{label}' is required.");
        }

        return new ECPoint(
            ParseBigInteger(payload.X, $"{label}.x"),
            ParseBigInteger(payload.Y, $"{label}.y"));
    }

    private static ImmutableArray<BigInteger> ParseBigIntegerArray(string[]? values, string label)
    {
        if (values is null || values.Length == 0)
        {
            throw new InvalidOperationException($"Fixture numeric array '{label}' is required.");
        }

        return values
            .Select((value, index) => ParseBigInteger(value, $"{label}[{index}]"))
            .ToImmutableArray();
    }

    private static BigInteger ParseBigInteger(string? value, string label)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"Fixture numeric field '{label}' is required.");
        }

        return BigInteger.Parse(value, CultureInfo.InvariantCulture);
    }

    private sealed record ControlledElectionFixturePackPayload(
        string? FixtureVersion,
        string? Profile,
        string? DecodeTier,
        string? DecodeBound,
        string? CircuitVersion,
        bool Deterministic,
        string? GeneratedAt,
        ControlledElectionPointPayload? PublicKey,
        ControlledElectionBallotPayload? Ballot,
        ControlledElectionBallotPayload? RerandomizedBallot,
        string[]? ExpectedAggregateTally,
        ControlledElectionTestOnlyPayload? TestOnly);

    private sealed record ControlledElectionPointPayload(
        string? X,
        string? Y);

    private sealed record ControlledElectionVectorCiphertextPayload(
        ControlledElectionPointPayload[]? C1,
        ControlledElectionPointPayload[]? C2);

    private sealed record ControlledElectionBallotPayload(
        int ChoiceIndex,
        int SelectionCount,
        ControlledElectionVectorCiphertextPayload? Ciphertext,
        string[]? Nonces,
        string[]? ExpectedPlaintextSlots);

    private sealed record ControlledElectionTestOnlyPayload(
        string? Seed,
        string? PrivateKey,
        string? EncryptionNonceSeed,
        string? RerandomizationNonceSeed);
}

internal sealed record LoadedControlledElectionFixturePack(
    string FixtureVersion,
    string Profile,
    string DecodeTier,
    BigInteger DecodeBound,
    string CircuitVersion,
    bool Deterministic,
    string GeneratedAt,
    ECPoint PublicKey,
    LoadedControlledElectionBallot Ballot,
    LoadedControlledElectionBallot RerandomizedBallot,
    ImmutableArray<BigInteger> ExpectedAggregateTally,
    LoadedControlledElectionTestOnlyMaterial TestOnly);

internal sealed record LoadedControlledElectionBallot(
    int ChoiceIndex,
    int SelectionCount,
    ControlledElectionBallot Ballot,
    ImmutableArray<BigInteger> Nonces,
    ImmutableArray<BigInteger> ExpectedPlaintextSlots);

internal sealed record LoadedControlledElectionTestOnlyMaterial(
    BigInteger Seed,
    BigInteger PrivateKey,
    BigInteger EncryptionNonceSeed,
    BigInteger RerandomizationNonceSeed);

internal sealed record ElectionCryptoFixtureVersionValidation(
    bool IsAccepted,
    string Status,
    string Notes)
{
    public static ElectionCryptoFixtureVersionValidation Accepted(string status, string notes) =>
        new(true, status, notes);

    public static ElectionCryptoFixtureVersionValidation Rejected(string status, string notes) =>
        new(false, status, notes);
}
