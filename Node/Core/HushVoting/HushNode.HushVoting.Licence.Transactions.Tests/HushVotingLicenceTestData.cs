// FEAT-015 Task 3.2 — deterministic licence signed-transaction builder.
//
// Mirrors FullIdentityTwinTestData conventions. K-001 is the FEAT-001 public corpus
// key vector (public replay, never a secret). Baseline/upgrade payloads reuse the
// Phase 2.4 fixed inputs so fixture-parity tests can diff byte-for-byte against the
// frozen artifact (LIC-FIX-001/002 canonical JSON + digests).

using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using static Olimpo.DigitalSignature;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public static class HushVotingLicenceTestData
{
    // FEAT-001 public corpus key K-001 (P-01 producer).
    public const string K001PrivateScalarHex = "6e3f74236c3d4a20553be05963f624696990c22245599b3d1b30262af793d885";
    public const string K001SigningAddress = "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5";

    public const string DirectFree = "hushvoting.direct.free";
    public const string Veritas500 = "hushvoting.veritas.500";
    public const string Veritas2000 = "hushvoting.veritas.2000";
    public const string Veritas10000 = "hushvoting.veritas.10000";
    public const string CatalogueV1 = "hushvoting-licence-catalogue/v1.0.0";

    public static readonly Guid BaselineTransactionId = Guid.Parse("5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e");
    public static readonly Guid UpgradeTransactionId = Guid.Parse("8c6a1b77-4d2e-4f91-a4c0-9e7b2d8f1a55");
    public static readonly Guid PayloadKind = HushVotingLicenceAssignmentPayloadHandler.LicenceAssignmentPayloadKind;

    public static readonly Timestamp CanonicalTimestamp = new(
        DateTime.Parse("2026-09-06T00:00:00.000Z", null, System.Globalization.DateTimeStyles.AssumeUniversal));

    public static HushVotingLicenceAssignmentPayload BaselinePayload() =>
        new(HushVotingLicenceTransitionIntent.BaselineFree, DirectFree, CatalogueV1);

    public static HushVotingLicenceAssignmentPayload UpgradePayload() =>
        new(
            HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
            Veritas2000,
            CatalogueV1,
            BaselineTransactionId,
            DirectFree);

    public static SignedTransaction<HushVotingLicenceAssignmentPayload> BuildSigned(
        HushVotingLicenceAssignmentPayload? payload = null,
        Guid? transactionId = null,
        Timestamp? timestamp = null,
        string? signatoryOverride = null,
        string? signatureOverride = null,
        string privateScalarHex = K001PrivateScalarHex,
        bool signCompact = true,
        long? payloadSizeOverride = null)
    {
        var actualPayload = payload ?? BaselinePayload();
        var size = HushVotingLicenceCanonicalJson.PayloadJsonUtf8Length(actualPayload);

        var unsigned = new UnsignedTransaction<HushVotingLicenceAssignmentPayload>(
            new TransactionId(transactionId ?? BaselineTransactionId),
            PayloadKind,
            timestamp ?? CanonicalTimestamp,
            actualPayload,
            payloadSizeOverride ?? size);

        var signatory = signatoryOverride ?? K001SigningAddress;
        var canonicalJson = new HushVotingLicenceCanonicalSerializer().SerializeCanonicalUnsignedJson(
            new SignedTransaction<HushVotingLicenceAssignmentPayload>(
                unsigned,
                new SignatureInfo(signatory, string.Empty)));

        var signature = signatureOverride
            ?? (signCompact
                ? SignMessageCompactBase64(canonicalJson, privateScalarHex)
                : SignMessage(canonicalJson, privateScalarHex));

        return new SignedTransaction<HushVotingLicenceAssignmentPayload>(
            unsigned,
            new SignatureInfo(signatory, signature));
    }
}
