// FEAT-011 Task 2.5 — canonical FullIdentity validation and signature contracts.
//
// Server-consumable contracts for the unchanged FullIdentity path:
//  - ContentValidationResult: the canonical typed validation outcome (no
//    expected exceptions, no null-as-success);
//  - FullIdentityValidationCodes: stable terminal/editable code registry;
//  - Approved signature-decoder contract (compact base64 r||s and the
//    explicitly Approved historical DER form; unknown encodings fail closed);
//  - canonical-message serializer contract (exact FEAT-001 unsigned JSON);
//  - bounded diagnostics contract (never full alias/address material).
//
// Wire representation is unchanged: RPC names, field names, payload-kind GUID
// (351cd60b-3fdf-48d4-b608-e93c0100f7d0), and signed JSON shape are pinned by
// FEAT-001 vectors. Phase 3 implements the validator; this file defines the
// contract and the pure vocabulary the implementation and tests compile
// against.

using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Identity.Model;

/// <summary>
/// Canonical typed transaction-content validation outcome. Expected validation
/// failures are DATA, never exceptions and never null success. Valid results
/// carry the typed validated content for downstream validator signing.
/// </summary>
public sealed record ContentValidationResult(
    bool IsValid,
    string? ValidationCode,
    string? Message,
    object? ValidatedContent = null)
{
    public static ContentValidationResult Valid(object validatedContent) =>
        new(true, null, null, validatedContent);

    public static ContentValidationResult Invalid(string code, string message) =>
        new(false, code, message, null);

    /// <summary>Alias-only correction is the only editable class; cryptographic/key/context failures are terminal.</summary>
    public bool IsEditable =>
        ValidationCode is not null && FullIdentityValidationCodes.IsEditable(ValidationCode);
}

/// <summary>
/// Stable FullIdentity validation-code registry. Codes are stable identifiers
/// for typed outcomes and safe diagnostics; they never contain message text,
/// aliases, addresses, or signatures.
/// </summary>
public static class FullIdentityValidationCodes
{
    // Terminal: shape/metadata
    public const string MalformedJson = "FULL_IDENTITY_MALFORMED_JSON";
    public const string UnsupportedKind = "FULL_IDENTITY_UNSUPPORTED_KIND";
    public const string InvalidTransactionId = "FULL_IDENTITY_INVALID_TRANSACTION_ID";
    public const string InvalidTimestamp = "FULL_IDENTITY_INVALID_TIMESTAMP";
    public const string InvalidPayloadSize = "FULL_IDENTITY_INVALID_PAYLOAD_SIZE";
    public const string UnsupportedContext = "FULL_IDENTITY_UNSUPPORTED_CONTEXT";

    // Editable: alias canonical-rule failures (allowlisted correction path)
    public const string AliasOutOfBounds = "FULL_IDENTITY_ALIAS_OUT_OF_BOUNDS";
    public const string AliasDisallowedCharacters = "FULL_IDENTITY_ALIAS_DISALLOWED_CHARACTERS";

    // Terminal: keys/addresses/signature
    public const string InvalidSigningAddress = "FULL_IDENTITY_INVALID_SIGNING_ADDRESS";
    public const string InvalidEncryptionAddress = "FULL_IDENTITY_INVALID_ENCRYPTION_ADDRESS";
    public const string SignatoryMismatch = "FULL_IDENTITY_SIGNATORY_MISMATCH";
    public const string UnsupportedSignatureEncoding = "FULL_IDENTITY_UNSUPPORTED_SIGNATURE_ENCODING";
    public const string InvalidSignature = "FULL_IDENTITY_INVALID_SIGNATURE";

    /// <summary>Allowlisted editable codes — the ONLY class that may re-enter explicit review.</summary>
    public static readonly IReadOnlySet<string> EditableCodes =
        new HashSet<string>(StringComparer.Ordinal) { AliasOutOfBounds, AliasDisallowedCharacters };

    public static bool IsEditable(string code) => EditableCodes.Contains(code);

    /// <summary>Terminal codes never permit correction or retry.</summary>
    public static bool IsTerminal(string code) =>
        !IsEditable(code) &&
        (code == MalformedJson || code == UnsupportedKind || code == InvalidTransactionId ||
         code == InvalidTimestamp || code == InvalidPayloadSize || code == UnsupportedContext ||
         code == InvalidSigningAddress || code == InvalidEncryptionAddress ||
         code == SignatoryMismatch || code == UnsupportedSignatureEncoding || code == InvalidSignature);
}

/// <summary>
/// Approved signature encodings (FEAT-001): compact base64 (r||s, 64 bytes,
/// current TS/Rust wire) and the explicitly Approved historical DER hex form.
/// Unknown/ambiguous encodings fail closed — never guessed.
/// </summary>
public enum ApprovedSignatureEncoding
{
    CompactBase64,
    Der,
}

/// <summary>Signature verification input — exact canonical bytes, never reordered.</summary>
public sealed record SignatureVerificationInput(
    string CanonicalUnsignedJson,
    string Signature,
    string SignatoryPublicSigningAddress);

/// <summary>Signature verification outcome with the approved encoding classification.</summary>
public sealed record SignatureVerificationOutcome(
    bool IsValid,
    ApprovedSignatureEncoding? Encoding,
    string? FailureCode)
{
    public static SignatureVerificationOutcome Valid(ApprovedSignatureEncoding encoding) =>
        new(true, encoding, null);

    public static SignatureVerificationOutcome UnsupportedEncoding() =>
        new(false, null, FullIdentityValidationCodes.UnsupportedSignatureEncoding);

    public static SignatureVerificationOutcome InvalidSignature() =>
        new(false, null, FullIdentityValidationCodes.InvalidSignature);
}

/// <summary>
/// Approved signature-decoder contract. Implementations verify over the exact
/// FEAT-001 canonical unsigned JSON and classify the approved encoding.
/// </summary>
public interface IFullIdentitySignatureVerifier
{
    SignatureVerificationOutcome Verify(SignatureVerificationInput input);
}

/// <summary>
/// Canonical-message contract: the unsigned JSON bytes whose hash the
/// signature binds. Property order and encoding are pinned by the FEAT-001
/// cross-runtime corpus; implementations must be byte-exact.
/// </summary>
public interface IFullIdentityCanonicalSerializer
{
    string SerializeCanonicalUnsignedJson(SignedTransaction<FullIdentityPayload> transaction);
}

/// <summary>
/// FullIdentity validator contract — validates the unchanged signed shape and
/// canonical message without expected exceptions. A valid result carries the
/// typed validated content so validator signing may proceed; every rejection
/// returns a stable terminal or editable code.
/// </summary>
public interface IFullIdentityValidator
{
    bool CanValidate(Guid transactionKind);

    ContentValidationResult Validate(SignedTransaction<FullIdentityPayload> transaction);
}

/// <summary>Bounded diagnostics contract — stable codes only, no sensitive material.</summary>
public sealed record FullIdentityDiagnostic(string ValidationCode);

/// <summary>
/// Pure signature-encoding classification (compact base64 r||s vs Approved
/// historical DER hex). Unknown/ambiguous input classifies as null and fails
/// closed; classification never decodes or verifies.
/// </summary>
public static class SignatureEncodingClassifier
{
    public const int CompactSignatureByteLength = 64;
    public const int DerMinimumByteLength = 70;
    public const int DerMaximumByteLength = 72;

    public static ApprovedSignatureEncoding? Classify(string signature)
    {
        if (string.IsNullOrWhiteSpace(signature))
        {
            return null;
        }

        // Compact base64: exactly 64 bytes (r||s).
        try
        {
            byte[] compact = Convert.FromBase64String(signature);
            if (compact.Length == CompactSignatureByteLength)
            {
                return ApprovedSignatureEncoding.CompactBase64;
            }
        }
        catch (FormatException)
        {
            // Not base64 — try the Approved DER hex form.
        }

        // Approved historical DER: hex, 70–72 bytes, sequence tag 0x30.
        if (TryParseHex(signature, out byte[] der) &&
            der.Length is >= DerMinimumByteLength and <= DerMaximumByteLength &&
            der[0] == 0x30)
        {
            return ApprovedSignatureEncoding.Der;
        }

        return null;
    }

    private static bool TryParseHex(string text, out byte[] bytes)
    {
        bytes = Array.Empty<byte>();
        if (text.Length % 2 != 0)
        {
            return false;
        }
        try
        {
            bytes = Convert.FromHexString(text);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}

public static class FullIdentityDiagnosticFactory
{
    /// <summary>Derives a bounded diagnostic from a validation result; never throws.</summary>
    public static FullIdentityDiagnostic From(ContentValidationResult result) =>
        new(result.ValidationCode ?? "FULL_IDENTITY_VALID");
}
