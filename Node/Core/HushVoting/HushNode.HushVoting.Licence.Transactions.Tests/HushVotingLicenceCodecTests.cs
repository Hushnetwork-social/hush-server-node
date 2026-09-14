// FEAT-015 Task 3.2 — canonical serializer, signature, and tamper tests.
//
// Proves the Phase 3.1 codec is byte-exact with the frozen Phase 2.4 corpus (fixture
// parity), reproduces the exact canonical payload size, verifies real signatures in
// both Approved encodings (compact base64 and DER), rejects tampered bytes /
// signatory mismatches / unknown encodings, and proves one-byte tamper detection
// end-to-end through the verifier.

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Xunit;
using static Olimpo.DigitalSignature;

namespace HushNode.HushVoting.Licence.Transactions.Tests;

public sealed class HushVotingLicenceCanonicalSerializerTests
{
    private readonly HushVotingLicenceCanonicalSerializer _serializer = new();

    [Fact]
    public void Baseline_serializer_output_is_byte_exact_with_the_frozen_corpus()
    {
        var tx = HushVotingLicenceTestData.BuildSigned(
            HushVotingLicenceTestData.BaselinePayload(),
            HushVotingLicenceTestData.BaselineTransactionId,
            HushVotingLicenceTestData.CanonicalTimestamp);

        var canonical = _serializer.SerializeCanonicalUnsignedJson(tx);

        canonical.Should().Be(
            "{\"TransactionId\":\"5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e\",\"PayloadKind\":\"71370664-5eb4-4ce9-b96a-d7e7ffe53db5\",\"TransactionTimeStamp\":\"2026-09-06T00:00:00.000Z\",\"Payload\":{\"TransitionIntent\":\"baseline_free\",\"RequestedPlanId\":\"hushvoting.direct.free\",\"ObservedCatalogueVersion\":\"hushvoting-licence-catalogue/v1.0.0\"},\"PayloadSize\":144}");

        // Digest parity with the frozen LIC-FIX-001 vector.
        Sha256Hex(canonical).Should().Be("a7e344b590e2eebc8b29d3b09fba0178e66e61a810756ae50a7942f4a76cd993");
    }

    [Fact]
    public void Upgrade_serializer_output_is_byte_exact_with_the_frozen_corpus()
    {
        var tx = HushVotingLicenceTestData.BuildSigned(
            HushVotingLicenceTestData.UpgradePayload(),
            HushVotingLicenceTestData.UpgradeTransactionId,
            HushVotingLicenceTestData.CanonicalTimestamp);

        var canonical = _serializer.SerializeCanonicalUnsignedJson(tx);

        canonical.Should().Be(
            "{\"TransactionId\":\"8c6a1b77-4d2e-4f91-a4c0-9e7b2d8f1a55\",\"PayloadKind\":\"71370664-5eb4-4ce9-b96a-d7e7ffe53db5\",\"TransactionTimeStamp\":\"2026-09-06T00:00:00.000Z\",\"Payload\":{\"TransitionIntent\":\"confirmed_upgrade\",\"RequestedPlanId\":\"hushvoting.veritas.2000\",\"ObservedCatalogueVersion\":\"hushvoting-licence-catalogue/v1.0.0\",\"ExpectedCurrentLicenceTransactionId\":\"5f2d9e11-3c44-4a80-b8e7-6b2f1a0c9d3e\",\"ExpectedCurrentPlanId\":\"hushvoting.direct.free\"},\"PayloadSize\":275}");

        Sha256Hex(canonical).Should().Be("27a380b4242bb06d3c6068953fff31f0a6179c0b80e73631e3b47b9ddcbe2cd0");
    }

    [Fact]
    public void Recorded_payload_size_matches_the_recomputed_canonical_size()
    {
        var tx = HushVotingLicenceTestData.BuildSigned(HushVotingLicenceTestData.UpgradePayload());

        _serializer.PayloadJsonUtf8Length(tx.Payload).Should().Be((int)tx.PayloadSize);
    }

    [Fact]
    public void One_byte_tamper_changes_digest_and_fails_verification()
    {
        // Tamper LIC-FIX-003: 'hushvoting.direct.free' -> 'hushvoting.direct.fred' (one byte).
        var tamperedPayload = new HushVotingLicenceAssignmentPayload(
            HushVotingLicenceTransitionIntent.BaselineFree,
            "hushvoting.direct.fred",
            HushVotingLicenceTestData.CatalogueV1);

        // Sign the TRUE baseline, then verify against the tampered payload's canonical bytes.
        var trueTx = HushVotingLicenceTestData.BuildSigned();
        var tamperedTx = HushVotingLicenceTestData.BuildSigned(
            tamperedPayload,
            HushVotingLicenceTestData.BaselineTransactionId,
            HushVotingLicenceTestData.CanonicalTimestamp,
            signatureOverride: trueTx.UserSignature.Signature);

        var tamperedCanonical = _serializer.SerializeCanonicalUnsignedJson(tamperedTx);
        var verifier = new HushVotingLicenceSignatureVerifier();
        var outcome = verifier.Verify(new HushVotingLicenceSignatureVerificationInput(
            tamperedCanonical,
            tamperedTx.UserSignature.Signature,
            tamperedTx.UserSignature.Signatory));

        outcome.IsValid.Should().BeFalse();
        outcome.FailureCode.Should().Be(HushVotingLicenceValidationCodes.SignatureInvalid);

        // Baseline canonical vs tampered canonical differ at exactly one byte.
        var baselineCanonical = _serializer.SerializeCanonicalUnsignedJson(trueTx);
        var baselineBytes = Encoding.UTF8.GetBytes(baselineCanonical);
        var tamperedBytes = Encoding.UTF8.GetBytes(tamperedCanonical);
        var differing = baselineBytes.Zip(tamperedBytes).Count(pair => pair.First != pair.Second);
        differing.Should().Be(1);
    }

    private static string Sha256Hex(string utf8Text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(utf8Text)));
}

public sealed class HushVotingLicenceSignatureVerifierTests
{
    private readonly HushVotingLicenceSignatureVerifier _verifier = new();
    private readonly HushVotingLicenceCanonicalSerializer _serializer = new();

    private HushVotingLicenceSignatureVerificationInput InputFor(
        SignedTransaction<HushVotingLicenceAssignmentPayload> tx) =>
        new(_serializer.SerializeCanonicalUnsignedJson(tx), tx.UserSignature.Signature, tx.UserSignature.Signatory);

    [Fact]
    public void Compact_base64_signature_over_baseline_verifies()
    {
        var tx = HushVotingLicenceTestData.BuildSigned(signCompact: true);

        var outcome = _verifier.Verify(InputFor(tx));

        outcome.IsValid.Should().BeTrue();
        outcome.Encoding.Should().Be(ApprovedSignatureEncoding.CompactBase64);
    }

    [Fact]
    public void Approved_der_signature_over_upgrade_verifies()
    {
        var tx = HushVotingLicenceTestData.BuildSigned(
            HushVotingLicenceTestData.UpgradePayload(),
            HushVotingLicenceTestData.UpgradeTransactionId,
            signCompact: false);

        var outcome = _verifier.Verify(InputFor(tx));

        outcome.IsValid.Should().BeTrue();
        outcome.Encoding.Should().Be(ApprovedSignatureEncoding.Der);
    }

    [Fact]
    public void Signature_over_changed_intent_fails()
    {
        var original = HushVotingLicenceTestData.BuildSigned();
        var changed = HushVotingLicenceTestData.BuildSigned(
            new HushVotingLicenceAssignmentPayload(
                HushVotingLicenceTransitionIntent.ConfirmedUpgrade,
                HushVotingLicenceTestData.Veritas2000,
                HushVotingLicenceTestData.CatalogueV1,
                HushVotingLicenceTestData.BaselineTransactionId,
                HushVotingLicenceTestData.DirectFree),
            signatureOverride: original.UserSignature.Signature);

        var outcome = _verifier.Verify(InputFor(changed));

        outcome.IsValid.Should().BeFalse();
        outcome.FailureCode.Should().Be(HushVotingLicenceValidationCodes.SignatureInvalid);
    }

    [Fact]
    public void Signature_over_changed_expected_current_precondition_fails()
    {
        var original = HushVotingLicenceTestData.BuildSigned(
            HushVotingLicenceTestData.UpgradePayload(),
            HushVotingLicenceTestData.UpgradeTransactionId);
        var changed = HushVotingLicenceTestData.BuildSigned(
            HushVotingLicenceTestData.UpgradePayload() with
            {
                ExpectedCurrentPlanId = HushVotingLicenceTestData.Veritas500,
            },
            HushVotingLicenceTestData.UpgradeTransactionId,
            signatureOverride: original.UserSignature.Signature);

        var outcome = _verifier.Verify(InputFor(changed));

        outcome.IsValid.Should().BeFalse();
        outcome.FailureCode.Should().Be(HushVotingLicenceValidationCodes.SignatureInvalid);
    }

    [Fact]
    public void Signatory_mismatch_fails()
    {
        // Sign with K-001 but present a different signatory (wrong public key).
        var tx = HushVotingLicenceTestData.BuildSigned(
            signatoryOverride: "0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048aa5");

        var outcome = _verifier.Verify(InputFor(tx));

        outcome.IsValid.Should().BeFalse();
        outcome.FailureCode.Should().Be(HushVotingLicenceValidationCodes.SignatureInvalid);
    }

    [Fact]
    public void Changed_transaction_timestamp_fails()
    {
        var original = HushVotingLicenceTestData.BuildSigned();
        var changedTimestamp = new HushShared.Blockchain.Model.Timestamp(
            DateTime.Parse("2026-09-06T00:00:01.000Z", null, System.Globalization.DateTimeStyles.AssumeUniversal));
        var changed = HushVotingLicenceTestData.BuildSigned(
            timestamp: changedTimestamp,
            signatureOverride: original.UserSignature.Signature);

        var outcome = _verifier.Verify(InputFor(changed));

        outcome.IsValid.Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("AAAA")] // valid base64 but not 64 bytes / not DER
    public void Unknown_or_ambiguous_signature_encodings_fail_closed(string signature)
    {
        var tx = HushVotingLicenceTestData.BuildSigned(signatureOverride: signature);

        var outcome = _verifier.Verify(InputFor(tx));

        outcome.IsValid.Should().BeFalse();
        outcome.FailureCode.Should().Be(HushVotingLicenceValidationCodes.SignatureInvalid);
        outcome.Encoding.Should().BeNull();
    }
}
