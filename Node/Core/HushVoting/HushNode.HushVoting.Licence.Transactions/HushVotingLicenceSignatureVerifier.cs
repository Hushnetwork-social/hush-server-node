// FEAT-015 Task 3.1 — payload-specific licence signature verifier.
//
// Verifies over the exact canonical licence unsigned JSON with the two Approved FEAT-001
// encodings (compact base64 r||s, and the explicitly Approved historical DER hex form)
// using the shared classifier and the Olimpo Approved primitives. Unknown/ambiguous
// encodings fail closed. This verifier NEVER delegates to the generic permissive
// BlockchainGrpcService.ValidateUserSignature shortcut.

using HushShared.Identity.Model;
using static Olimpo.DigitalSignature;

namespace HushNode.HushVoting.Licence.Transactions;

public sealed class HushVotingLicenceSignatureVerifier : IHushVotingLicenceSignatureVerifier
{
    public HushVotingLicenceSignatureVerificationOutcome Verify(
        HushVotingLicenceSignatureVerificationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var encoding = SignatureEncodingClassifier.Classify(input.Signature);
        if (encoding is null)
        {
            return HushVotingLicenceSignatureVerificationOutcome.UnsupportedEncoding();
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
            ? HushVotingLicenceSignatureVerificationOutcome.Valid(encoding.Value)
            : HushVotingLicenceSignatureVerificationOutcome.InvalidSignature();
    }
}
