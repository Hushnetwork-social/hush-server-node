// FEAT-011 Task 3.2/3.4/3.6 — shared deterministic FullIdentity test data.
//
// Key pair K-001 comes from the FEAT-001 public corpus
// (conformance/identity/v1/vectors/key-vectors.json) — public vector replay,
// never a secret. Signatures are produced over the exact canonical unsigned
// JSON with the Olimpo Approved primitives (compact base64 and DER).

using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using static Olimpo.DigitalSignature;

namespace HushNode.Identity.Tests;

public static class FullIdentityTestData
{
    // FEAT-001 key vector K-001 (P-01 producer).
    public const string K001PrivateScalarHex = "6e3f74236c3d4a20553be05963f624696990c22245599b3d1b30262af793d885";
    public const string K001SigningAddress = "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5";
    public const string K001EncryptAddress = "032ebaf076203f15ac8119cfdbc9394d1c7b9929b0647e4f607e27da95701f8556";

    public const string Alias = "public-test-alias-001";

    public static readonly Guid PayloadKind = FullIdentityPayloadHandler.FullIdentityPayloadKind;

    public static readonly Timestamp CanonicalTimestamp = new(
        DateTime.Parse("2026-08-01T12:34:56.789Z", null, System.Globalization.DateTimeStyles.AssumeUniversal));

    public static readonly Guid CanonicalTransactionId = Guid.Parse("d4a2f9c1-3b5e-4f6a-9c7d-2e8f1a0b3c4d");

    /// <summary>The exact FEAT-001 canonical unsigned JSON for the K-001 vector (CB-001).</summary>
    public static string CanonicalUnsignedJson(SignedTransaction<FullIdentityPayload> transaction) =>
        new FullIdentityCanonicalSerializer().SerializeCanonicalUnsignedJson(transaction);

    public static SignedTransaction<FullIdentityPayload> BuildSigned(
        string alias = Alias,
        string signingAddress = K001SigningAddress,
        string encryptAddress = K001EncryptAddress,
        bool isPublic = true,
        string? signatoryOverride = null,
        string? signatureOverride = null,
        Guid? transactionId = null,
        Timestamp? timestamp = null,
        string privateScalarHex = K001PrivateScalarHex,
        bool signCompact = true)
    {
        var payload = new FullIdentityPayload(alias, signingAddress, encryptAddress, isPublic);
        var canonical = new FullIdentityCanonicalSerializer();
        var payloadSize = canonical.PayloadJsonUtf8Length(alias, signingAddress, encryptAddress, isPublic);

        var unsigned = new UnsignedTransaction<FullIdentityPayload>(
            new TransactionId(transactionId ?? CanonicalTransactionId),
            PayloadKind,
            timestamp ?? CanonicalTimestamp,
            payload,
            payloadSize);

        var canonicalJson = canonical.SerializeCanonicalUnsignedJson(new SignedTransaction<FullIdentityPayload>(
            unsigned,
            new SignatureInfo(signingAddress, string.Empty)));

        var signature = signatureOverride
            ?? (signCompact
                ? SignMessageCompactBase64(canonicalJson, privateScalarHex)
                : SignMessage(canonicalJson, privateScalarHex));

        return new SignedTransaction<FullIdentityPayload>(
            unsigned,
            new SignatureInfo(signatoryOverride ?? signingAddress, signature));
    }
}
