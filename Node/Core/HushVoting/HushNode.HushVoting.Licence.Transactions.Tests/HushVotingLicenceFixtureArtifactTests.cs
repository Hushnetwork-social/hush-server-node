// FEAT-015 Task 2.4 — canonical fixture artifact freeze tests.
//
// The fixture artifact (`Fixtures/v1.0.0/licence-transaction-vectors.json`) is the public,
// byte-identical, cross-runtime corpus for the sole licence transaction kind. These tests:
//   - load the committed artifact (never regenerated in-test);
//   - prove each frozen canonical JSON string equals the bytes produced by
//     HushVotingLicenceCanonicalJson for the same fixed inputs;
//   - recompute the lower-case SHA-256 hex digest and assert it matches the frozen value;
//   - prove the one-byte tamper vector differs from the baseline vector at exactly one byte.
//
// .NET, TypeScript, and Rust producers MUST reproduce these bytes/digests exactly; the
// artifact is immutable (additive versions only).

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public sealed class HushVotingLicenceFixtureArtifactTests
{
    private const string ArtifactRelativePath = "Fixtures/licence-transaction-vectors.json";

    private static JsonDocument LoadArtifact()
    {
        var path = Path.Combine(AppContext.BaseDirectory, ArtifactRelativePath);
        if (!File.Exists(path))
        {
            throw new Xunit.Sdk.XunitException($"Fixture artifact missing at {path}");
        }

        return JsonDocument.Parse(File.ReadAllBytes(path));
    }

    private static JsonElement FindVector(JsonDocument artifact, string id)
    {
        foreach (var vector in artifact.RootElement.GetProperty("vectors").EnumerateArray())
        {
            if (vector.GetProperty("id").GetString() == id)
            {
                return vector;
            }
        }

        throw new Xunit.Sdk.XunitException($"Fixture vector {id} not found.");
    }

    [Fact]
    public void Artifact_declares_the_frozen_payload_kind_guid()
    {
        using var artifact = LoadArtifact();

        artifact.RootElement.GetProperty("payloadKindGuid").GetString()
            .Should().Be("71370664-5eb4-4ce9-b96a-d7e7ffe53db5");
        artifact.RootElement.GetProperty("kind").GetString()
            .Should().Be("HushVotingLicenceAssignmentPayload");
    }

    [Fact]
    public void Artifact_contains_baseline_upgrade_and_one_byte_tamper_vectors()
    {
        using var artifact = LoadArtifact();

        var ids = artifact.RootElement.GetProperty("vectors")
            .EnumerateArray()
            .Select(vector => vector.GetProperty("id").GetString())
            .ToArray();

        ids.Should().Contain(new[] { "LIC-FIX-001", "LIC-FIX-002", "LIC-FIX-003" });
    }

    [Fact]
    public void Baseline_vector_canonical_bytes_match_the_canonical_writer_and_frozen_digest()
    {
        using var artifact = LoadArtifact();
        var vector = FindVector(artifact, "LIC-FIX-001");

        var frozenJson = vector.GetProperty("expected").GetProperty("canonicalUnsignedJson").GetString()!;
        var frozenDigest = vector.GetProperty("expected").GetProperty("sha256Hex").GetString()!;
        var frozenPayloadSize = vector.GetProperty("expected").GetProperty("payloadSizeBytes").GetInt64();

        var payload = new HushVotingLicenceAssignmentPayload(
            "baseline_free",
            "hushvoting.direct.free",
            "hushvoting-licence-catalogue/v1.0.0");

        // The canonical writer must reproduce the frozen artifact bytes exactly.
        var computedPayloadJson = HushVotingLicenceCanonicalJson.BuildPayloadJson(payload);
        var computed = HushVotingLicenceCanonicalJson.BuildCanonicalUnsignedJson(
            Guid.Parse("5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e"),
            Guid.Parse("71370664-5eb4-4ce9-b96a-d7e7ffe53db5"),
            DateTime.Parse("2026-09-06T00:00:00.000Z").ToUniversalTime(),
            payload,
            HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));

        computed.Should().Be(frozenJson);
        HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload).Should().Be((int)frozenPayloadSize);
        Sha256Hex(computed).Should().Be(frozenDigest);
    }

    [Fact]
    public void Upgrade_vector_canonical_bytes_match_the_canonical_writer_and_frozen_digest()
    {
        using var artifact = LoadArtifact();
        var vector = FindVector(artifact, "LIC-FIX-002");

        var frozenJson = vector.GetProperty("expected").GetProperty("canonicalUnsignedJson").GetString()!;
        var frozenDigest = vector.GetProperty("expected").GetProperty("sha256Hex").GetString()!;
        var frozenPayloadSize = vector.GetProperty("expected").GetProperty("payloadSizeBytes").GetInt64();

        var payload = new HushVotingLicenceAssignmentPayload(
            "confirmed_upgrade",
            "hushvoting.veritas.2000",
            "hushvoting-licence-catalogue/v1.0.0",
            Guid.Parse("5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e"),
            "hushvoting.direct.free");

        var computed = HushVotingLicenceCanonicalJson.BuildCanonicalUnsignedJson(
            Guid.Parse("8c6a1b77-4d2e-4f91-a4c0-9e7b2d8f1a55"),
            Guid.Parse("71370664-5eb4-4ce9-b96a-d7e7ffe53db5"),
            DateTime.Parse("2026-09-06T00:00:00.000Z").ToUniversalTime(),
            payload,
            HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload));

        computed.Should().Be(frozenJson);
        HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(payload).Should().Be((int)frozenPayloadSize);
        Sha256Hex(computed).Should().Be(frozenDigest);
    }

    [Fact]
    public void One_byte_tamper_vector_differs_from_baseline_at_exactly_one_byte()
    {
        using var artifact = LoadArtifact();
        var baseline = FindVector(artifact, "LIC-FIX-001");
        var tamper = FindVector(artifact, "LIC-FIX-003");

        var baselineJson = baseline.GetProperty("expected").GetProperty("canonicalUnsignedJson").GetString()!;
        var tamperJson = tamper.GetProperty("expected").GetProperty("canonicalUnsignedJson").GetString()!;
        var tamperDigest = tamper.GetProperty("expected").GetProperty("sha256Hex").GetString()!;

        var baselineBytes = Encoding.UTF8.GetBytes(baselineJson);
        var tamperBytes = Encoding.UTF8.GetBytes(tamperJson);

        tamperBytes.Length.Should().Be(baselineBytes.Length);
        var differingIndexes = baselineBytes
            .Zip(tamperBytes)
            .Select((pair, index) => new { pair.First, pair.Second, Index = index })
            .Where(item => item.First != item.Second)
            .Select(item => item.Index)
            .ToArray();

        differingIndexes.Should().HaveCount(1, "the tamper vector must differ from the baseline by exactly one byte");
        Sha256Hex(tamperJson).Should().Be(tamperDigest);
        Sha256Hex(tamperJson).Should().NotBe(Sha256Hex(baselineJson));
    }

    private static string Sha256Hex(string utf8Text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(utf8Text)));
}
