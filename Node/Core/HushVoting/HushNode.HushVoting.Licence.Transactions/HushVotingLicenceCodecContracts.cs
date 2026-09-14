// FEAT-015 Task 3.1 — canonical licence codec contracts.
//
// The licence codec reproduces the established Hush signing contract for the licence
// payload kind:
//   - canonical unsigned JSON bytes come from the frozen HushVotingLicenceCanonicalJson
//     writer (single byte owner; never a second serializer);
//   - PayloadSize is recomputed as the exact UTF-8 byte length of the payload JSON and
//     must equal the recorded transaction PayloadSize;
//   - the real user signature is verified over those exact canonical bytes with the
//     Approved FEAT-001 encodings (compact base64 r||s, and the Approved DER hex form)
//     via the shared classifier — never the permissive generic signature helper.

using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Canonical unsigned-JSON contract for a signed licence transaction.</summary>
public interface IHushVotingLicenceCanonicalSerializer
{
    /// <summary>Exact canonical unsigned JSON whose hash the user signature binds.</summary>
    string SerializeCanonicalUnsignedJson(SignedTransaction<HushVotingLicenceAssignmentPayload> transaction);

    /// <summary>Exact UTF-8 payload-JSON length (the PayloadSize rule).</summary>
    int PayloadJsonUtf8Length(HushVotingLicenceAssignmentPayload payload);
}

/// <summary>Payload-specific signature verification input over exact canonical bytes.</summary>
public sealed record HushVotingLicenceSignatureVerificationInput(
    string CanonicalUnsignedJson,
    string Signature,
    string SignatoryPublicSigningAddress);

/// <summary>Payload-specific signature verification outcome with the approved-encoding classification.</summary>
public sealed record HushVotingLicenceSignatureVerificationOutcome(
    bool IsValid,
    ApprovedSignatureEncoding? Encoding,
    string? FailureCode)
{
    public static HushVotingLicenceSignatureVerificationOutcome Valid(ApprovedSignatureEncoding encoding) =>
        new(true, encoding, null);

    public static HushVotingLicenceSignatureVerificationOutcome UnsupportedEncoding() =>
        new(false, null, HushVotingLicenceValidationCodes.SignatureInvalid);

    public static HushVotingLicenceSignatureVerificationOutcome InvalidSignature() =>
        new(false, null, HushVotingLicenceValidationCodes.SignatureInvalid);
}

/// <summary>
/// Payload-specific signature verification contract. Implementations verify the exact user
/// signature over the exact canonical licence unsigned JSON with the Approved encodings and
/// classify the encoding; unknown/ambiguous encodings fail closed. NEVER delegates to the
/// generic permissive signature helper.
/// </summary>
public interface IHushVotingLicenceSignatureVerifier
{
    HushVotingLicenceSignatureVerificationOutcome Verify(
        HushVotingLicenceSignatureVerificationInput input);
}
