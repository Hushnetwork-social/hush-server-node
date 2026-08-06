// FEAT-011 Task 2.6 — cross-runtime vector, mutation, and validation-code
// contract tests for the FullIdentity validation contracts (Task 2.5).
//
// Covers: stable code registry semantics (editable vs terminal), signature
// encoding classification (compact base64 / Approved DER / unknown), result
// construction invariants (no null success, no expected exceptions), bounded
// diagnostics, and the unchanged payload-kind GUID pin.

using FluentAssertions;
using HushShared.Blockchain.TransactionModel;
using HushShared.Identity.Model;
using Xunit;

namespace HushNode.Identity.Tests;

public sealed class FullIdentityValidationContractsTests
{
    [Fact]
    public void ContentValidationResult_Valid_CarriesTypedContentWithoutCode()
    {
        var content = FullIdentityPayloadHandler.CreateNew("alice", "signing", "encryption", true);
        var result = ContentValidationResult.Valid(content);

        result.IsValid.Should().BeTrue();
        result.ValidationCode.Should().BeNull();
        result.Message.Should().BeNull();
        result.ValidatedContent.Should().BeSameAs(content);
        result.IsEditable.Should().BeFalse();
    }

    [Fact]
    public void ContentValidationResult_Invalid_IsDataNeverNullSuccess()
    {
        var result = ContentValidationResult.Invalid(
            FullIdentityValidationCodes.InvalidSignature,
            "signature verification failed");

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.InvalidSignature);
        result.ValidatedContent.Should().BeNull();
        result.IsEditable.Should().BeFalse();
    }

    [Theory]
    [InlineData(FullIdentityValidationCodes.AliasOutOfBounds)]
    [InlineData(FullIdentityValidationCodes.AliasDisallowedCharacters)]
    public void EditableAllowlist_ContainsOnlyAliasCanonicalFailures(string code)
    {
        FullIdentityValidationCodes.IsEditable(code).Should().BeTrue();
        FullIdentityValidationCodes.IsTerminal(code).Should().BeFalse();
    }

    [Theory]
    [InlineData(FullIdentityValidationCodes.MalformedJson)]
    [InlineData(FullIdentityValidationCodes.UnsupportedKind)]
    [InlineData(FullIdentityValidationCodes.InvalidTransactionId)]
    [InlineData(FullIdentityValidationCodes.InvalidTimestamp)]
    [InlineData(FullIdentityValidationCodes.InvalidPayloadSize)]
    [InlineData(FullIdentityValidationCodes.UnsupportedContext)]
    [InlineData(FullIdentityValidationCodes.InvalidSigningAddress)]
    [InlineData(FullIdentityValidationCodes.InvalidEncryptionAddress)]
    [InlineData(FullIdentityValidationCodes.SignatoryMismatch)]
    [InlineData(FullIdentityValidationCodes.UnsupportedSignatureEncoding)]
    [InlineData(FullIdentityValidationCodes.InvalidSignature)]
    [InlineData(FullIdentityValidationCodes.Conflict)]
    public void TerminalCodes_AreNeverEditable(string code)
    {
        FullIdentityValidationCodes.IsEditable(code).Should().BeFalse();
        FullIdentityValidationCodes.IsTerminal(code).Should().BeTrue();
    }

    [Fact]
    public void UnknownCode_FailsClosed_NeverEditableOrTerminal()
    {
        FullIdentityValidationCodes.IsEditable("FULL_IDENTITY_UNKNOWN_CODE").Should().BeFalse();
        FullIdentityValidationCodes.IsTerminal("FULL_IDENTITY_UNKNOWN_CODE").Should().BeFalse();
    }

    [Fact]
    public void EditableResult_IsMarkedEditable_WhileTerminalIsNot()
    {
        var editable = ContentValidationResult.Invalid(FullIdentityValidationCodes.AliasOutOfBounds, "alias");
        var terminal = ContentValidationResult.Invalid(FullIdentityValidationCodes.InvalidSignature, "sig");

        editable.IsEditable.Should().BeTrue();
        terminal.IsEditable.Should().BeFalse();
    }

    [Fact]
    public void SignatureEncodingClassifier_RecognizesCompactBase64()
    {
        var compact = new byte[64];
        for (var i = 0; i < compact.Length; i++)
        {
            compact[i] = (byte)(i + 1);
        }

        SignatureEncodingClassifier.Classify(Convert.ToBase64String(compact))
            .Should()
            .Be(ApprovedSignatureEncoding.CompactBase64);
    }

    [Fact]
    public void SignatureEncodingClassifier_RecognizesApprovedDerHex()
    {
        // DER ECDSA: 30 45 | 02 21 <32> | 02 20 <32>  → 70 bytes, 0x30 tag.
        var der = new byte[70];
        der[0] = 0x30;
        der[1] = 0x45;
        der[2] = 0x02;
        der[3] = 0x21;
        der[4] = 0x01; // first r byte (nonzero by construction)
        der[37] = 0x02;
        der[38] = 0x20;
        der[39] = 0x01;

        SignatureEncodingClassifier.Classify(Convert.ToHexString(der))
            .Should()
            .Be(ApprovedSignatureEncoding.Der);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-signature")]
    [InlineData("AQID")] // 3 bytes base64 — neither 64-byte compact nor hex DER
    [InlineData("3030303030303030303030303030303030303030303030303030303030303030")] // 48 bytes hex, wrong length
    public void SignatureEncodingClassifier_UnknownInput_FailsClosed(string signature)
    {
        SignatureEncodingClassifier.Classify(signature).Should().BeNull();
    }

    [Fact]
    public void SignatureEncodingClassifier_CompactWinsOverHexShapedInput()
    {
        // 64 bytes of 0x30-ish content is valid compact base64 AND would be a
        // 64-byte "hex string" of 32 bytes — classification must prefer the
        // exact 64-byte compact interpretation (compact is checked first).
        var bytes = new byte[64];
        Array.Fill(bytes, (byte)0x30);
        var base64 = Convert.ToBase64String(bytes);

        SignatureEncodingClassifier.Classify(base64).Should().Be(ApprovedSignatureEncoding.CompactBase64);
    }

    [Fact]
    public void SignatureOutcomes_MapToStableCodes()
    {
        var ok = SignatureVerificationOutcome.Valid(ApprovedSignatureEncoding.CompactBase64);
        ok.IsValid.Should().BeTrue();
        ok.Encoding.Should().Be(ApprovedSignatureEncoding.CompactBase64);
        ok.FailureCode.Should().BeNull();

        var unsupported = SignatureVerificationOutcome.UnsupportedEncoding();
        unsupported.IsValid.Should().BeFalse();
        unsupported.FailureCode.Should().Be(FullIdentityValidationCodes.UnsupportedSignatureEncoding);

        var invalid = SignatureVerificationOutcome.InvalidSignature();
        invalid.FailureCode.Should().Be(FullIdentityValidationCodes.InvalidSignature);
    }

    [Fact]
    public void Diagnostics_AreBoundedAndNeverThrow()
    {
        var valid = FullIdentityDiagnosticFactory.From(ContentValidationResult.Valid(new object()));
        valid.ValidationCode.Should().Be("FULL_IDENTITY_VALID");

        var invalid = FullIdentityDiagnosticFactory.From(
            ContentValidationResult.Invalid(FullIdentityValidationCodes.MalformedJson, "x"));
        invalid.ValidationCode.Should().Be(FullIdentityValidationCodes.MalformedJson);
    }

    [Fact]
    public void FullIdentityPayloadKindGuid_IsUnchanged()
    {
        FullIdentityPayloadHandler.FullIdentityPayloadKind.Should().Be(
            Guid.Parse("351cd60b-3fdf-48d4-b608-e93c0100f7d0"));
    }
}
