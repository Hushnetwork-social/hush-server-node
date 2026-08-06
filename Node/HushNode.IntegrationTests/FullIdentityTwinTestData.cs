// FEAT-011 Task 3.8 — deterministic FullIdentity signed-transaction builder
// for the focused TwinTests (FEAT-001 key vector K-001; public corpus replay).

using HushNode.Identity;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using static Olimpo.DigitalSignature;

namespace HushNode.IntegrationTests;

public static class FullIdentityTwinTestData
{
    public const string K001PrivateScalarHex = "6e3f74236c3d4a20553be05963f624696990c22245599b3d1b30262af793d885";
    public const string K001SigningAddress = "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5";
    public const string K001EncryptAddress = "032ebaf076203f15ac8119cfdbc9394d1c7b9929b0647e4f607e27da95701f8556";

    public const string Alias = "public-test-alias-001";

    public static readonly Timestamp CanonicalTimestamp = new(
        DateTime.Parse("2026-08-01T12:34:56.789Z", null, System.Globalization.DateTimeStyles.AssumeUniversal));

    public static SignedTransaction<FullIdentityPayload> BuildSigned(
        string? signatureOverride = null)
    {
        var canonical = new FullIdentityCanonicalSerializer();
        var payload = new FullIdentityPayload(Alias, K001SigningAddress, K001EncryptAddress, true);
        var payloadSize = canonical.PayloadJsonUtf8Length(Alias, K001SigningAddress, K001EncryptAddress, true);

        var unsigned = new UnsignedTransaction<FullIdentityPayload>(
            new TransactionId(Guid.Parse("d4a2f9c1-3b5e-4f6a-9c7d-2e8f1a0b3c4d")),
            FullIdentityPayloadHandler.FullIdentityPayloadKind,
            CanonicalTimestamp,
            payload,
            payloadSize);

        var canonicalJson = canonical.SerializeCanonicalUnsignedJson(new SignedTransaction<FullIdentityPayload>(
            unsigned,
            new SignatureInfo(K001SigningAddress, string.Empty)));

        var signature = signatureOverride ?? SignMessageCompactBase64(canonicalJson, K001PrivateScalarHex);

        return new SignedTransaction<FullIdentityPayload>(
            unsigned,
            new SignatureInfo(K001SigningAddress, signature));
    }
}
