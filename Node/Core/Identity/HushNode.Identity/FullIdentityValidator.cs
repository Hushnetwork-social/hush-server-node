// FEAT-011 Task 3.1 — canonical FullIdentity content validator.
//
// Validates the unchanged signed FullIdentity shape against the Phase 2
// contract: transaction metadata, payload size (exact UTF-8 payload JSON
// length), canonical alias rules (NFC-trim, 1–64 grapheme clusters, ≤256
// UTF-8 bytes, disallowed controls), Approved address encodings, signatory
// ownership, and the Approved signature over the exact FEAT-001 canonical
// unsigned message. Expected failures are typed ContentValidationResult
// values — never exceptions, never null success.

using System.Globalization;
using System.Text;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.Identity;

public sealed class FullIdentityValidator(
    IFullIdentityCanonicalSerializer canonicalSerializer,
    IFullIdentitySignatureVerifier signatureVerifier) : IFullIdentityValidator
{
    private readonly IFullIdentityCanonicalSerializer _canonicalSerializer = canonicalSerializer;
    private readonly IFullIdentitySignatureVerifier _signatureVerifier = signatureVerifier;

    // Disallowed control/bidi/invisible set (mirrors the FEAT-007 client rule).
    private static readonly HashSet<int> DisallowedCodePoints = new()
    {
        0x0000, 0x0001, 0x0002, 0x0003, 0x0004, 0x0005, 0x0006, 0x0007, 0x0008, 0x000B, 0x000C,
        0x000E, 0x000F, 0x0010, 0x0011, 0x0012, 0x0013, 0x0014, 0x0015, 0x0016, 0x0017, 0x0018,
        0x0019, 0x001A, 0x001B, 0x001C, 0x001D, 0x001E, 0x001F, 0x007F,
        0x200B, 0x200C, 0x200D, 0x200E, 0x200F, 0x202A, 0x202B, 0x202C, 0x202D, 0x202E,
        0x2066, 0x2067, 0x2068, 0x2069, 0xFEFF,
    };

    public bool CanValidate(Guid transactionKind) =>
        FullIdentityPayloadHandler.FullIdentityPayloadKind == transactionKind;

    public ContentValidationResult Validate(SignedTransaction<FullIdentityPayload> transaction)
    {
        var payload = transaction.Payload;

        // Transaction metadata bounds.
        if (transaction.TransactionId == TransactionId.Empty ||
            string.IsNullOrWhiteSpace(transaction.UserSignature.Signatory))
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.InvalidTransactionId,
                "Transaction id or signatory is empty.");
        }

        // Payload size must equal the exact UTF-8 payload JSON length.
        var canonicalSize = _canonicalSerializer.PayloadJsonUtf8Length(
            payload.IdentityAlias, payload.PublicSigningAddress, payload.PublicEncryptAddress, payload.IsPublic);
        if (transaction.PayloadSize != canonicalSize)
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.InvalidPayloadSize,
                "Payload size does not match the canonical payload JSON length.");
        }

        // Canonical alias rules (editable class only).
        var aliasOutcome = ValidateAlias(payload.IdentityAlias);
        if (aliasOutcome is not null)
        {
            return aliasOutcome;
        }

        // Approved address encodings (terminal class).
        if (!IsApprovedAddress(payload.PublicSigningAddress))
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.InvalidSigningAddress,
                "Public signing address is not an approved encoding.");
        }

        if (!IsApprovedAddress(payload.PublicEncryptAddress))
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.InvalidEncryptionAddress,
                "Public encrypt address is not an approved encoding.");
        }

        // Signatory ownership (terminal class).
        if (!string.Equals(payload.PublicSigningAddress, transaction.UserSignature.Signatory, StringComparison.Ordinal))
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.SignatoryMismatch,
                "Signatory does not match the payload signing address.");
        }

        // Approved signature over the exact canonical unsigned message.
        var canonicalJson = _canonicalSerializer.SerializeCanonicalUnsignedJson(transaction);
        var signatureOutcome = _signatureVerifier.Verify(new SignatureVerificationInput(
            canonicalJson,
            transaction.UserSignature.Signature,
            transaction.UserSignature.Signatory));
        if (!signatureOutcome.IsValid)
        {
            return ContentValidationResult.Invalid(
                signatureOutcome.FailureCode ?? FullIdentityValidationCodes.InvalidSignature,
                "Signature verification failed.");
        }

        return ContentValidationResult.Valid(transaction);
    }

    private static ContentValidationResult? ValidateAlias(string? alias)
    {
        if (alias is null)
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.AliasOutOfBounds,
                "Alias is null.");
        }

        string normalized;
        try
        {
            normalized = alias.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.AliasOutOfBounds,
                "Alias cannot be normalized.");
        }

        var trimmed = normalized.Trim();
        if (trimmed.Length == 0)
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.AliasOutOfBounds,
                "Alias is empty after trim.");
        }

        foreach (var rune in trimmed.EnumerateRunes())
        {
            if (DisallowedCodePoints.Contains(rune.Value))
            {
                return ContentValidationResult.Invalid(
                    FullIdentityValidationCodes.AliasDisallowedCharacters,
                    "Alias contains a disallowed character.");
            }
        }

        var graphemes = CountGraphemes(trimmed);
        if (graphemes < 1 || graphemes > 64)
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.AliasOutOfBounds,
                "Alias grapheme count is out of bounds.");
        }

        if (Encoding.UTF8.GetByteCount(trimmed) > 256)
        {
            return ContentValidationResult.Invalid(
                FullIdentityValidationCodes.AliasOutOfBounds,
                "Alias exceeds the UTF-8 byte bound.");
        }

        return null;
    }

    /// <summary>Approved secp256k1 address encodings: 66-char compressed or 130-char uncompressed hex.</summary>
    private static bool IsApprovedAddress(string? address)
    {
        if (string.IsNullOrEmpty(address))
        {
            return false;
        }

        if (address.Length is not (66 or 130))
        {
            return false;
        }

        return address.All(Uri.IsHexDigit);
    }

    /// <summary>Grapheme-cluster counting via StringInfo (NFC text elements).</summary>
    private static int CountGraphemes(string value) =>
        new StringInfo(value).LengthInTextElements;
}
