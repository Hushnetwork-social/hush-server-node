using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using HushShared.Elections.Model;

namespace HushShared.Elections.Verification.Model;

public sealed record Sp07PackagePublicStatementHashInput(
    string ElectionId,
    string ChunkId,
    string ProtocolPackageHash,
    string BallotDefinitionHash,
    Sp07PackagePublicPoint PublicKey,
    int Ballots,
    int Slots,
    string AcceptedBallotSetHash,
    string PublishedBallotStreamHash);

public sealed record Sp07PackagePublicPoint(
    string X,
    string Y);

public static class Sp07PackagePublicStatementHasher
{
    private static readonly JsonSerializerOptions CompactJson = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    public static string ComputeStatementHashSha512(Sp07PackagePublicStatementHashInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.PublicKey);

        var statement = new CanonicalStatement(
            "HushSp07BgStatementV1",
            ElectionSp07ProfileIds.ProofConstruction,
            "hush_babyjubjub_vector_ballot_bg_adapter_v1",
            "hush_babyjubjub_bn254_subgroup_v1",
            "hush_pedersen_commitment_key_demo_v1",
            "hush_sp07_fiat_shamir_sha512_v1",
            "matrix_m_1_publication_proof_v1",
            input.ElectionId,
            input.ChunkId,
            input.ProtocolPackageHash,
            input.BallotDefinitionHash,
            new CanonicalPoint(input.PublicKey.X, input.PublicKey.Y),
            input.Ballots,
            input.Slots,
            MatrixM: 1,
            MatrixN: input.Ballots,
            input.AcceptedBallotSetHash,
            input.PublishedBallotStreamHash);

        var bytes = JsonSerializer.SerializeToUtf8Bytes(statement, CompactJson);
        return Convert.ToHexString(SHA512.HashData(bytes)).ToLowerInvariant();
    }

    private sealed record CanonicalStatement(
        [property: JsonPropertyName("schema")] string Schema,
        [property: JsonPropertyName("construction")] string Construction,
        [property: JsonPropertyName("adapter")] string Adapter,
        [property: JsonPropertyName("group_profile")] string GroupProfile,
        [property: JsonPropertyName("commitment_key_profile")] string CommitmentKeyProfile,
        [property: JsonPropertyName("fiat_shamir_profile")] string FiatShamirProfile,
        [property: JsonPropertyName("proof_profile")] string ProofProfile,
        [property: JsonPropertyName("election_id")] string ElectionId,
        [property: JsonPropertyName("chunk_id")] string ChunkId,
        [property: JsonPropertyName("protocol_package_hash")] string ProtocolPackageHash,
        [property: JsonPropertyName("ballot_definition_hash")] string BallotDefinitionHash,
        [property: JsonPropertyName("public_key")] CanonicalPoint PublicKey,
        [property: JsonPropertyName("ballots")] int Ballots,
        [property: JsonPropertyName("slots")] int Slots,
        [property: JsonPropertyName("matrix_m")] int MatrixM,
        [property: JsonPropertyName("matrix_n")] int MatrixN,
        [property: JsonPropertyName("accepted_ballot_set_hash")] string AcceptedBallotSetHash,
        [property: JsonPropertyName("published_ballot_stream_hash")] string PublishedBallotStreamHash);

    private sealed record CanonicalPoint(
        [property: JsonPropertyName("x")] string X,
        [property: JsonPropertyName("y")] string Y);
}
