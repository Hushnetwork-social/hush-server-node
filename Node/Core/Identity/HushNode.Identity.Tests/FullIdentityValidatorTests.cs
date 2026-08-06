// FEAT-011 Task 3.2 — canonical FullIdentity content validation tests:
// FEAT-001 canonical vector byte match, compact/DER signatures, wrong
// key/message, every field mutation, alias Unicode bounds, address encodings,
// signatory ownership, stable validation codes, and no exception paths.

using FluentAssertions;
using HushShared.Blockchain.TransactionModel;
using HushShared.Identity.Model;
using Xunit;

namespace HushNode.Identity.Tests;

public sealed class FullIdentityValidatorTests
{
    private readonly FullIdentityValidator _sut = new(
        new FullIdentityCanonicalSerializer(),
        new FullIdentitySignatureVerifier());

    [Fact]
    public void CanonicalUnsignedJson_MatchesFeat001VectorByteExact()
    {
        var transaction = FullIdentityTestData.BuildSigned();

        var json = FullIdentityTestData.CanonicalUnsignedJson(transaction);

        // CB-001 vector: exact historical TypeScript producer bytes.
        json.Should().Be(
            "{\"TransactionId\":\"d4a2f9c1-3b5e-4f6a-9c7d-2e8f1a0b3c4d\"," +
            "\"PayloadKind\":\"351cd60b-3fdf-48d4-b608-e93c0100f7d0\"," +
            "\"TransactionTimeStamp\":\"2026-08-01T12:34:56.789Z\"," +
            "\"Payload\":{\"IdentityAlias\":\"public-test-alias-001\"," +
            "\"PublicSigningAddress\":\"0237fdd4364c0b898908be2f1a98a6b4a7890c623ae92a283640e44d87e048daa5\"," +
            "\"PublicEncryptAddress\":\"032ebaf076203f15ac8119cfdbc9394d1c7b9929b0647e4f607e27da95701f8556\"," +
            "\"IsPublic\":true},\"PayloadSize\":241}");
    }

    [Fact]
    public void ValidCompactSignature_IsValid()
    {
        var transaction = FullIdentityTestData.BuildSigned();
        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeTrue();
        result.ValidatedContent.Should().BeSameAs(transaction);
    }

    [Fact]
    public void ValidDerSignature_IsValid()
    {
        var transaction = FullIdentityTestData.BuildSigned(signCompact: false);
        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void MutatedAlias_AfterSigning_IsInvalidSignature()
    {
        // Sign the original bytes, then mutate the payload AFTER signing so the
        // signature no longer binds the canonical message.
        var signed = FullIdentityTestData.BuildSigned();
        var transaction = signed with { Payload = signed.Payload with { IdentityAlias = "public-test-alias-002" } };

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.InvalidSignature);
    }

    [Fact]
    public void WrongSigner_FailsClosedWithSignatoryMismatch()
    {
        var transaction = FullIdentityTestData.BuildSigned(signatoryOverride: FullIdentityTestData.K001EncryptAddress);

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.SignatoryMismatch);
    }

    [Fact]
    public void SignatureFromWrongKey_IsInvalidSignature()
    {
        // Same message, different private key — signature must not verify.
        var transaction = FullIdentityTestData.BuildSigned(
            privateScalarHex: "0000000000000000000000000000000000000000000000000000000000000001");

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.InvalidSignature);
    }

    [Fact]
    public void MalformedSignatureEncoding_IsUnsupportedEncoding()
    {
        var transaction = FullIdentityTestData.BuildSigned(signatureOverride: "not-a-signature");

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.UnsupportedSignatureEncoding);
    }

    [Fact]
    public void PayloadSizeMismatch_IsInvalidPayloadSize()
    {
        var transaction = FullIdentityTestData.BuildSigned() with
        {
            PayloadSize = FullIdentityTestData.CanonicalUnsignedJson(FullIdentityTestData.BuildSigned()).Length + 1,
        };

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.InvalidPayloadSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")] // 65 graphemes
    [InlineData("bad\u0000alias")] // NUL control
    public void InvalidAlias_ReturnsEditableOutOfBoundsOrDisallowed(string alias)
    {
        var transaction = FullIdentityTestData.BuildSigned(alias: alias);

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.IsEditable.Should().BeTrue();
        result.ValidationCode.Should().BeOneOf(
            FullIdentityValidationCodes.AliasOutOfBounds,
            FullIdentityValidationCodes.AliasDisallowedCharacters);
    }

    [Fact]
    public void SixtyFourEmojiAlias_ExactlyAtByteBound_IsValid()
    {
        // 64 graphemes (surrogate-pair emoji), 256 UTF-8 bytes — exactly at the byte bound.
        var emojiAlias = string.Concat(Enumerable.Repeat(char.ConvertFromUtf32(0x1F600), 64));
        var transaction = FullIdentityTestData.BuildSigned(alias: emojiAlias);

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("12345")] // too short
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")] // non-hex 66
    [InlineData("00")] // wrong length, valid hex
    public void InvalidSigningAddress_IsTerminal(string address)
    {
        var transaction = FullIdentityTestData.BuildSigned(signingAddress: address);

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.InvalidSigningAddress);
        result.IsEditable.Should().BeFalse();
    }

    [Fact]
    public void UncompressedApprovedAddress_IsAccepted()
    {
        // 130-char uncompressed hex is an Approved encoding (FEAT-001 P-02 form).
        var uncompressed = "04" + new string('a', 128);
        var transaction = FullIdentityTestData.BuildSigned(
            signingAddress: uncompressed,
            signatoryOverride: uncompressed);

        var result = _sut.Validate(transaction);

        // Signature cannot verify for this synthetic key, but the address
        // encoding itself must NOT be rejected as invalid: expect the
        // signature stage (terminal InvalidSignature), not the address stage.
        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.InvalidSignature);
    }

    [Fact]
    public void EmptyTransactionId_IsInvalidTransactionId()
    {
        var transaction = FullIdentityTestData.BuildSigned(transactionId: Guid.Empty);

        var result = _sut.Validate(transaction);

        result.IsValid.Should().BeFalse();
        result.ValidationCode.Should().Be(FullIdentityValidationCodes.InvalidTransactionId);
    }

    [Fact]
    public void ExpectedFailures_AreTypedData_NeverExceptions()
    {
        // Every mutation above must return a result object, never throw.
        var mutations = new[]
        {
            FullIdentityTestData.BuildSigned(alias: string.Empty),
            FullIdentityTestData.BuildSigned(signatureOverride: "!!"),
            FullIdentityTestData.BuildSigned(signingAddress: "zz"),
            FullIdentityTestData.BuildSigned(transactionId: Guid.Empty),
        };

        foreach (var mutation in mutations)
        {
            var result = _sut.Validate(mutation);
            result.IsValid.Should().BeFalse();
        }
    }

    [Fact]
    public void CanValidate_OnlyAcceptsTheFullIdentityKind()
    {
        _sut.CanValidate(FullIdentityPayloadHandler.FullIdentityPayloadKind).Should().BeTrue();
        _sut.CanValidate(Guid.Parse("00000000-0000-0000-0000-000000000001")).Should().BeFalse();
    }
}
