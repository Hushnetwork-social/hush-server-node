using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HushShared.Elections.Model;
using HushShared.Elections.PublicationProof;

namespace HushNode.Elections;

public interface IElectionSp07ProductionProofInputBuilder
{
    ElectionSp07ProductionProofInputBuildResult Build(
        ElectionId electionId,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        IReadOnlyList<ElectionPublicationWitnessRecord> witnesses,
        Sp07PublicationChunkPlan plan);
}

public sealed record ElectionSp07ProductionProofInputBuildResult(
    bool IsSuccessful,
    string? FailureCode,
    string? FailureReason,
    Guid? WitnessSetId,
    int AcceptedBallotCount,
    int PublishedBallotCount,
    int EncryptedSlotCount,
    IReadOnlyDictionary<string, Sp07RustWorkerProductionProofInput> InputsByChunkId,
    string? ElectionPublicKeyId = null)
{
    public static ElectionSp07ProductionProofInputBuildResult Success(
        Guid witnessSetId,
        int acceptedBallotCount,
        int publishedBallotCount,
        int encryptedSlotCount,
        string electionPublicKeyId,
        IReadOnlyDictionary<string, Sp07RustWorkerProductionProofInput> inputsByChunkId) =>
        new(
            true,
            null,
            null,
            witnessSetId,
            acceptedBallotCount,
            publishedBallotCount,
            encryptedSlotCount,
            inputsByChunkId,
            electionPublicKeyId);

    public static ElectionSp07ProductionProofInputBuildResult Failure(
        string failureCode,
        string failureReason) =>
        new(
            false,
            failureCode,
            failureReason,
            null,
            0,
            0,
            0,
            new Dictionary<string, Sp07RustWorkerProductionProofInput>(StringComparer.Ordinal),
            null);
}

public sealed class ElectionSp07ProductionProofInputBuilder(
    IElectionPublicationWitnessEnvelopeCrypto envelopeCrypto) : IElectionSp07ProductionProofInputBuilder
{
    private const string WitnessVersion = "sp07-publication-rerandomization-witness-v1";
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IElectionPublicationWitnessEnvelopeCrypto _envelopeCrypto =
        envelopeCrypto ?? throw new ArgumentNullException(nameof(envelopeCrypto));

    public ElectionSp07ProductionProofInputBuildResult Build(
        ElectionId electionId,
        IReadOnlyList<ElectionAcceptedBallotRecord> acceptedBallots,
        IReadOnlyList<ElectionPublishedBallotRecord> publishedBallots,
        IReadOnlyList<ElectionPublicationWitnessRecord> witnesses,
        Sp07PublicationChunkPlan plan)
    {
        ArgumentNullException.ThrowIfNull(acceptedBallots);
        ArgumentNullException.ThrowIfNull(publishedBallots);
        ArgumentNullException.ThrowIfNull(witnesses);
        ArgumentNullException.ThrowIfNull(plan);

        if (acceptedBallots.Count == 0)
        {
            return Failure("sp07_production_input_empty", "SP-07 production proof input requires accepted ballots.");
        }

        if (acceptedBallots.Count != publishedBallots.Count)
        {
            return Failure(
                "sp07_production_input_count_mismatch",
                "SP-07 production proof input requires matching accepted and published ballot counts.");
        }

        if (plan.AcceptedBallotCount != acceptedBallots.Count)
        {
            return Failure(
                "sp07_production_input_plan_mismatch",
                "SP-07 production proof input chunk plan does not match the accepted-ballot count.");
        }

        if (witnesses.Count != publishedBallots.Count)
        {
            return Failure(
                "sp07_production_input_witness_mismatch",
                "SP-07 production proof input requires one sealed publication witness per published ballot.");
        }

        if (witnesses.Any(x => x.CustodyStatus != ElectionPublicationWitnessCustodyStatus.Sealed))
        {
            return Failure(
                "sp07_production_input_witness_not_sealed",
                "SP-07 production proof input can only be built while every publication witness remains sealed.");
        }

        var witnessSetIds = witnesses.Select(x => x.WitnessSetId).Distinct().ToArray();
        if (witnessSetIds.Length != 1)
        {
            return Failure(
                "sp07_production_input_witness_set_mismatch",
                "SP-07 production proof input v1 requires one witness set for the proof session.");
        }

        if (acceptedBallots.Select(x => x.Id).Distinct().Count() != acceptedBallots.Count)
        {
            return Failure(
                "sp07_production_input_accepted_duplicate",
                "SP-07 production proof input accepted ballot inventory contains duplicate ids.");
        }

        var acceptedById = acceptedBallots.ToDictionary(x => x.Id);
        var acceptedOrder = acceptedBallots
            .Select((ballot, index) => new { ballot.Id, Index = index })
            .ToDictionary(x => x.Id, x => x.Index);
        var witnessAcceptedIds = witnesses.Select(x => x.AcceptedBallotId).ToArray();
        if (witnessAcceptedIds.Distinct().Count() != acceptedBallots.Count ||
            witnessAcceptedIds.Any(id => !acceptedById.ContainsKey(id)))
        {
            return Failure(
                "sp07_production_input_witness_accepted_mismatch",
                "SP-07 publication witnesses do not cover exactly the accepted ballots.");
        }

        if (publishedBallots.Select(x => x.PublicationSequence).Distinct().Count() != publishedBallots.Count)
        {
            return Failure(
                "sp07_production_input_published_duplicate",
                "SP-07 production proof input published ballot stream contains duplicate publication sequences.");
        }

        if (witnesses.Any(x => !x.PublishedSequence.HasValue) ||
            witnesses.Select(x => x.PublishedSequence).Distinct().Count() != witnesses.Count)
        {
            return Failure(
                "sp07_production_input_witness_published_mismatch",
                "SP-07 publication witnesses require unique published ballot sequence bindings.");
        }

        var publishedBySequence = publishedBallots.ToDictionary(x => x.PublicationSequence);
        var witnessesBySequence = witnesses
            .Where(x => x.PublishedSequence.HasValue)
            .ToDictionary(x => x.PublishedSequence!.Value);
        if (witnessesBySequence.Count != publishedBallots.Count ||
            publishedBySequence.Keys.Any(sequence => !witnessesBySequence.ContainsKey(sequence)))
        {
            return Failure(
                "sp07_production_input_witness_published_mismatch",
                "SP-07 publication witnesses do not cover exactly the published ballot stream.");
        }

        var parsedAccepted = new Dictionary<Guid, ParsedCipherBallot>();
        foreach (var acceptedBallot in acceptedBallots)
        {
            if (!TryParseBallot(
                    acceptedBallot.EncryptedBallotPackage,
                    $"accepted ballot {acceptedBallot.Id:N}",
                    out var parsed,
                    out var error))
            {
                return Failure("sp07_production_input_invalid_ballot", error);
            }

            parsedAccepted[acceptedBallot.Id] = parsed;
        }

        var evidenceRows = new List<PublicationEvidenceRow>(publishedBallots.Count);
        foreach (var publishedBallot in publishedBallots.OrderBy(x => x.PublicationSequence))
        {
            var witness = witnessesBySequence[publishedBallot.PublicationSequence];
            var acceptedBallot = acceptedById[witness.AcceptedBallotId];
            var acceptedParsed = parsedAccepted[acceptedBallot.Id];

            if (!TryParseBallot(
                    publishedBallot.EncryptedBallotPackage,
                    $"published ballot {publishedBallot.PublicationSequence}",
                    out var publishedParsed,
                    out var error))
            {
                return Failure("sp07_production_input_invalid_ballot", error);
            }

            if (!HashMatches(witness.AcceptedEncryptedBallotHash, acceptedParsed.PackageHashSha256Lower) ||
                !HashMatches(witness.PublishedEncryptedBallotHash, publishedParsed.PackageHashSha256Lower))
            {
                return Failure(
                    "sp07_production_input_witness_hash_mismatch",
                    "SP-07 publication witness hashes do not match the accepted/published ballot packages.");
            }

            var unseal = TryUnsealWitness(electionId, witness, out var witnessMaterial, out error);
            if (!unseal)
            {
                return Failure("sp07_production_input_witness_unseal_failed", error);
            }

            if (!HashMatches(witness.SealedWitnessMaterialHash, ComputeSha512Lower(witnessMaterial)))
            {
                return Failure(
                    "sp07_production_input_witness_material_hash_mismatch",
                    "SP-07 publication witness material hash does not match the sealed custody record.");
            }

            if (!TryParseWitnessMaterial(
                    witnessMaterial,
                    witness,
                    acceptedParsed,
                    publishedParsed,
                    out var nonces,
                    out error))
            {
                return Failure("sp07_production_input_invalid_witness", error);
            }

            evidenceRows.Add(new PublicationEvidenceRow(
                acceptedBallot,
                publishedBallot,
                acceptedParsed,
                publishedParsed,
                acceptedOrder[acceptedBallot.Id],
                nonces));
        }

        if (!TryValidateCommonEnvelope(evidenceRows, plan, out var publicKey, out var slotCount, out var envelopeError))
        {
            return Failure("sp07_production_input_envelope_mismatch", envelopeError);
        }

        var inputsByChunkId = new Dictionary<string, Sp07RustWorkerProductionProofInput>(StringComparer.Ordinal);
        foreach (var chunk in plan.Chunks.OrderBy(x => x.ChunkIndex))
        {
            var chunkRows = evidenceRows
                .Skip(chunk.Offset)
                .Take(chunk.Count)
                .ToArray();
            if (chunkRows.Length != chunk.Count)
            {
                return Failure(
                    "sp07_production_input_plan_mismatch",
                    $"SP-07 chunk {chunk.ChunkId} could not be filled from the published ballot stream.");
            }

            var acceptedChunkRows = chunkRows
                .OrderBy(x => x.AcceptedGlobalIndex)
                .ToArray();
            var acceptedIndexById = acceptedChunkRows
                .Select((row, index) => new { row.AcceptedBallot.Id, Index = index })
                .ToDictionary(x => x.Id, x => x.Index);

            inputsByChunkId[chunk.ChunkId] = new Sp07RustWorkerProductionProofInput(
                publicKey,
                acceptedChunkRows.Select(x => x.AcceptedParsed.Payload).ToArray(),
                chunkRows.Select(x => x.PublishedParsed.Payload).ToArray(),
                chunkRows.Select(x => acceptedIndexById[x.AcceptedBallot.Id]).ToArray(),
                chunkRows.Select(x => x.RerandomizationNonces).ToArray());
        }

        return ElectionSp07ProductionProofInputBuildResult.Success(
            witnessSetIds[0],
            acceptedBallots.Count,
            publishedBallots.Count,
            slotCount,
            ComputePublicKeyId(publicKey),
            inputsByChunkId);
    }

    private static ElectionSp07ProductionProofInputBuildResult Failure(
        string code,
        string reason) =>
        ElectionSp07ProductionProofInputBuildResult.Failure(code, reason);

    private bool TryUnsealWitness(
        ElectionId electionId,
        ElectionPublicationWitnessRecord witness,
        out string witnessMaterial,
        out string error)
    {
        witnessMaterial = string.Empty;
        error = string.Empty;
        try
        {
            witnessMaterial = _envelopeCrypto.UnsealWitnessMaterial(
                witness.SealedWitnessMaterial,
                electionId,
                witness.Id);
            return !string.IsNullOrWhiteSpace(witnessMaterial);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or CryptographicException)
        {
            error = $"SP-07 publication witness {witness.Id:N} could not be unsealed: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseBallot(
        string package,
        string label,
        out ParsedCipherBallot parsed,
        out string error)
    {
        parsed = default!;
        error = string.Empty;
        try
        {
            var payload = JsonSerializer.Deserialize<PublishedElectionBallotPackage>(package, JsonOptions);
            if (payload?.PublicKey is null ||
                payload.Ciphertext?.C1 is null ||
                payload.Ciphertext.C2 is null ||
                payload.SelectionCount <= 0 ||
                payload.Ciphertext.C1.Length != payload.SelectionCount ||
                payload.Ciphertext.C2.Length != payload.SelectionCount)
            {
                error = $"SP-07 {label} has an invalid ciphertext envelope.";
                return false;
            }

            var slots = payload.Ciphertext.C1
                .Select((c1, index) => new Sp07CipherSlotPayload(
                    ToSp07Point(c1, $"{label}.ciphertext.c1[{index}]"),
                    ToSp07Point(payload.Ciphertext.C2[index], $"{label}.ciphertext.c2[{index}]")))
                .ToArray();
            parsed = new ParsedCipherBallot(
                ToSp07Point(payload.PublicKey, $"{label}.publicKey"),
                new Sp07CipherBallotPayload(slots),
                payload.SelectionCount,
                ComputeSha256Lower(package));
            return true;
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or ArgumentException)
        {
            error = $"SP-07 {label} could not be parsed: {ex.Message}";
            return false;
        }
    }

    private static bool TryParseWitnessMaterial(
        string witnessMaterial,
        ElectionPublicationWitnessRecord witness,
        ParsedCipherBallot acceptedParsed,
        ParsedCipherBallot publishedParsed,
        out IReadOnlyList<string> nonces,
        out string error)
    {
        nonces = Array.Empty<string>();
        error = string.Empty;
        try
        {
            var payload = JsonSerializer.Deserialize<PublicationRerandomizationWitnessMaterial>(
                witnessMaterial,
                JsonOptions);
            if (payload is null ||
                !string.Equals(payload.Version, WitnessVersion, StringComparison.Ordinal) ||
                payload.RerandomizationNonces is null ||
                payload.RerandomizationNonces.Count != acceptedParsed.SelectionCount ||
                payload.SelectionCount != acceptedParsed.SelectionCount)
            {
                error = $"SP-07 publication witness {witness.Id:N} has an invalid rerandomization envelope.";
                return false;
            }

            if (!HashMatches(payload.AcceptedEncryptedBallotHash, acceptedParsed.PackageHashSha256Lower) ||
                !HashMatches(payload.PublishedEncryptedBallotHash, publishedParsed.PackageHashSha256Lower) ||
                !HashMatches(payload.AcceptedEncryptedBallotHash, witness.AcceptedEncryptedBallotHash) ||
                !HashMatches(payload.PublishedEncryptedBallotHash, witness.PublishedEncryptedBallotHash))
            {
                error = $"SP-07 publication witness {witness.Id:N} does not bind the expected ballot hashes.";
                return false;
            }

            var normalizedNonces = payload.RerandomizationNonces
                .Select(x => x?.Trim() ?? string.Empty)
                .ToArray();
            if (normalizedNonces.Any(string.IsNullOrWhiteSpace))
            {
                error = $"SP-07 publication witness {witness.Id:N} contains an empty rerandomization nonce.";
                return false;
            }

            nonces = normalizedNonces;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"SP-07 publication witness {witness.Id:N} could not be parsed: {ex.Message}";
            return false;
        }
    }

    private static bool TryValidateCommonEnvelope(
        IReadOnlyList<PublicationEvidenceRow> rows,
        Sp07PublicationChunkPlan plan,
        out Sp07PointPayload publicKey,
        out int slotCount,
        out string error)
    {
        publicKey = default!;
        slotCount = 0;
        error = string.Empty;
        if (rows.Count == 0)
        {
            error = "SP-07 production proof input has no publication evidence rows.";
            return false;
        }

        publicKey = rows[0].AcceptedParsed.PublicKey;
        slotCount = rows[0].AcceptedParsed.SelectionCount;
        if (slotCount != plan.EncryptedSlotCount)
        {
            error = "SP-07 production proof input slot count does not match the chunk plan.";
            return false;
        }

        foreach (var row in rows)
        {
            if (row.AcceptedParsed.SelectionCount != slotCount ||
                row.PublishedParsed.SelectionCount != slotCount ||
                !PointEquals(row.AcceptedParsed.PublicKey, publicKey) ||
                !PointEquals(row.PublishedParsed.PublicKey, publicKey))
            {
                error = "SP-07 production proof input requires every accepted and published ballot to share the same public key and slot count.";
                return false;
            }
        }

        return true;
    }

    private static Sp07PointPayload ToSp07Point(PublishedElectionPointPayload? point, string label)
    {
        if (point is null ||
            string.IsNullOrWhiteSpace(point.X) ||
            string.IsNullOrWhiteSpace(point.Y))
        {
            throw new InvalidOperationException($"SP-07 point '{label}' is required.");
        }

        return new Sp07PointPayload(point.X.Trim(), point.Y.Trim());
    }

    private static bool PointEquals(Sp07PointPayload left, Sp07PointPayload right) =>
        string.Equals(left.X, right.X, StringComparison.Ordinal) &&
        string.Equals(left.Y, right.Y, StringComparison.Ordinal);

    private static string ComputePublicKeyId(Sp07PointPayload publicKey) =>
        $"babyjubjub-elgamal-pk-{ComputeSha256Lower($"{publicKey.X}|{publicKey.Y}")[..16]}";

    private static bool HashMatches(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) &&
        !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string ComputeSha256Lower(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private static string ComputeSha512Lower(string value) =>
        Convert.ToHexString(SHA512.HashData(Encoding.UTF8.GetBytes(value ?? string.Empty))).ToLowerInvariant();

    private sealed record ParsedCipherBallot(
        Sp07PointPayload PublicKey,
        Sp07CipherBallotPayload Payload,
        int SelectionCount,
        string PackageHashSha256Lower);

    private sealed record PublicationEvidenceRow(
        ElectionAcceptedBallotRecord AcceptedBallot,
        ElectionPublishedBallotRecord PublishedBallot,
        ParsedCipherBallot AcceptedParsed,
        ParsedCipherBallot PublishedParsed,
        int AcceptedGlobalIndex,
        IReadOnlyList<string> RerandomizationNonces);

    private sealed record PublishedElectionBallotPackage(
        string? Version,
        PublishedElectionPointPayload? PublicKey,
        int SelectionCount,
        PublishedElectionCiphertext? Ciphertext);

    private sealed record PublishedElectionCiphertext(
        PublishedElectionPointPayload[] C1,
        PublishedElectionPointPayload[] C2);

    private sealed record PublishedElectionPointPayload(string X, string Y);

    private sealed record PublicationRerandomizationWitnessMaterial(
        string? Version,
        string? AcceptedEncryptedBallotHash,
        string? PublishedEncryptedBallotHash,
        string? SourceProofBundleHash,
        string? PublishedProofBundleHash,
        int SelectionCount,
        IReadOnlyList<string>? RerandomizationNonces);
}
