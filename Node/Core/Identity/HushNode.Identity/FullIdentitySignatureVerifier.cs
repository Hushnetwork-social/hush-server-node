// FEAT-011 Task 3.1 — Approved FullIdentity signature verifier.
//
// Verifies over the exact FEAT-001 canonical unsigned JSON with the two
// Approved encodings (compact base64 r||s, and the explicitly Approved
// historical DER hex form). Unknown/ambiguous encodings fail closed. The
// signatory public key is the payload's PublicSigningAddress (ownership
// equality is enforced by the validator before verification).

using HushShared.Blockchain.TransactionModel;
using HushShared.Identity.Model;
using static Olimpo.DigitalSignature;

namespace HushNode.Identity;

public sealed class FullIdentitySignatureVerifier : IFullIdentitySignatureVerifier
{
    public SignatureVerificationOutcome Verify(SignatureVerificationInput input)
    {
        var encoding = SignatureEncodingClassifier.Classify(input.Signature);
        if (encoding is null)
        {
            return SignatureVerificationOutcome.UnsupportedEncoding();
        }

        var verified = encoding switch
        {
            ApprovedSignatureEncoding.CompactBase64 => VerifyCompactSignatureBase64(
                input.CanonicalUnsignedJson,
                input.Signature,
                input.SignatoryPublicSigningAddress),
            ApprovedSignatureEncoding.Der => VerifySignature(
                input.CanonicalUnsignedJson,
                input.Signature,
                input.SignatoryPublicSigningAddress),
            _ => false,
        };

        return verified
            ? SignatureVerificationOutcome.Valid(encoding.Value)
            : SignatureVerificationOutcome.InvalidSignature();
    }
}
