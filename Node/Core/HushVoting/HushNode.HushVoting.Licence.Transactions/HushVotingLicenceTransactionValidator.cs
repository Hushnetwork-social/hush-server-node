// FEAT-015 Task 3.3 — dependency-safe licence transaction validation ports + composite validator.
//
// Mirrors FullIdentityValidator + IFullIdentityAdmissionService: the composite validator takes a
// signed licence transaction and resolves server-owned inputs through small ports that the host
// adapters implement in Phase 6 (indexed identity provenance from identity storage, current
// immutable catalogue snapshot, and current indexed entitlement via the licence index projection).
// The core decision itself (HushVotingLicenceTransitionDecisionCore) stays pure and never performs
// I/O; this class only sequences kind/size/signature/shape checks with the resolved inputs and
// returns typed ContentValidationResult-style outcomes. Expected failures are data, never
// exceptions, and the stable 20-code registry is used verbatim.

using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.HushVoting.Licensing.Model;
using HushShared.Identity.Model;

namespace HushNode.HushVoting.Licence.Transactions;

/// <summary>Resolved signatory identity facts (host adapter supplies these in Phase 6).</summary>
public sealed record HushVotingLicenceSignatoryContext(
    string CanonicalPublicSigningAddress,
    long IdentityCreationBlockIndex);

/// <summary>Server-owned catalogue + current-state resolution port.</summary>
public interface IHushVotingLicenceValidationContextSource
{
    /// <summary>Current immutable catalogue snapshot (host loads the FEAT-012 release).</summary>
    Task<HushVotingLicenceCatalogue> GetCurrentCatalogueAsync(CancellationToken cancellationToken);

    /// <summary>Resolves the exact indexed identity for a canonical signatory address; null when not found.</summary>
    Task<HushVotingLicenceSignatoryContext?> ResolveIdentityAsync(
        string canonicalPublicSigningAddress,
        CancellationToken cancellationToken);

    /// <summary>Resolves the current indexed effective state for the identity (never writes).</summary>
    Task<HushVotingLicenceCurrentState> ResolveCurrentStateAsync(
        HushVotingLicenceSignatoryContext identity,
        CancellationToken cancellationToken);
}

/// <summary>Canonical-address normalization for signatory comparison (trim + invariant lower).</summary>
public static class HushVotingLicenceCanonicalAddress
{
    public static string? Normalize(string? rawAddress)
    {
        if (string.IsNullOrWhiteSpace(rawAddress))
        {
            return null;
        }

        var canonical = rawAddress.Trim().ToLowerInvariant();
        return canonical.Length == 0 ? null : canonical;
    }
}

/// <summary>
/// Composite licence transaction validator. Order is fixed: payload kind → shape → recorded size
/// vs canonical payload length → real user signature over exact canonical bytes → identity
/// resolution → catalogue staleness → current state → pure transition decision. Authentication
/// and identity resolution failures happen before any transition semantics.
/// </summary>
public sealed class HushVotingLicenceTransactionValidator(
    IHushVotingLicenceCanonicalSerializer canonicalSerializer,
    IHushVotingLicenceSignatureVerifier signatureVerifier,
    IHushVotingLicenceValidationContextSource contextSource)
    : IHushVotingLicenceTransactionValidator
{
    private readonly IHushVotingLicenceCanonicalSerializer _canonicalSerializer = canonicalSerializer;
    private readonly IHushVotingLicenceSignatureVerifier _signatureVerifier = signatureVerifier;
    private readonly IHushVotingLicenceValidationContextSource _contextSource = contextSource;

    public bool CanValidate(Guid transactionKind) =>
        HushVotingLicenceAssignmentPayloadHandler.IsLicencePayloadKind(transactionKind);

    public async Task<ContentValidationResult> ValidateAsync(
        SignedTransaction<HushVotingLicenceAssignmentPayload> transaction,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(transaction.Payload);

        // Payload kind (fail closed before any semantics).
        if (!HushVotingLicenceAssignmentPayloadHandler.IsLicencePayloadKind(transaction.PayloadKind))
        {
            return ContentValidationResult.Invalid(
                HushVotingLicenceValidationCodes.PayloadKindUnsupported,
                "Transaction payload kind is not the licence assignment kind.");
        }

        // Closed shape + bounds.
        var shape = HushVotingLicencePayloadShapeGuard.Validate(transaction.Payload);
        if (!shape.IsValid)
        {
            return ContentValidationResult.Invalid(
                shape.ValidationCode ?? HushVotingLicenceValidationCodes.PayloadMalformed,
                shape.Message ?? "Licence payload shape is invalid.");
        }

        // Recorded size must equal the exact canonical payload length.
        var recomputedSize = _canonicalSerializer.PayloadJsonUtf8Length(transaction.Payload);
        if (transaction.PayloadSize != recomputedSize)
        {
            return ContentValidationResult.Invalid(
                HushVotingLicenceValidationCodes.PayloadSizeMismatch,
                "Recorded payload size does not match the canonical payload length.");
        }

        // Exact canonical unsigned JSON + real user signature (never the permissive shortcut).
        var canonicalJson = _canonicalSerializer.SerializeCanonicalUnsignedJson(transaction);
        var signatory = HushVotingLicenceCanonicalAddress.Normalize(transaction.UserSignature.Signatory);
        if (signatory is null)
        {
            return ContentValidationResult.Invalid(
                HushVotingLicenceValidationCodes.SignatureInvalid,
                "Signatory is empty or not canonical.");
        }

        var signatureOutcome = _signatureVerifier.Verify(new HushVotingLicenceSignatureVerificationInput(
            canonicalJson,
            transaction.UserSignature.Signature,
            signatory));
        if (!signatureOutcome.IsValid)
        {
            return ContentValidationResult.Invalid(
                signatureOutcome.FailureCode ?? HushVotingLicenceValidationCodes.SignatureInvalid,
                "Licence transaction signature verification failed.");
        }

        // Exact indexed identity owned by the signatory (before any licence semantics).
        var identity = await _contextSource.ResolveIdentityAsync(signatory, cancellationToken);
        if (identity is null)
        {
            return ContentValidationResult.Invalid(
                HushVotingLicenceValidationCodes.SignatoryIdentityNotFound,
                "The signatory does not own an exact indexed HushNetwork identity.");
        }

        // Catalogue staleness: observed immutable release must equal the current catalogue.
        var catalogue = await _contextSource.GetCurrentCatalogueAsync(cancellationToken);
        if (!string.Equals(
                catalogue.Version.Value,
                transaction.Payload.ObservedCatalogueVersion,
                StringComparison.Ordinal))
        {
            return ContentValidationResult.Invalid(
                HushVotingLicenceValidationCodes.CatalogueStale,
                "The observed catalogue version is not the current immutable release.");
        }

        // Current indexed state then pure server-owned transition decision.
        var currentState = await _contextSource.ResolveCurrentStateAsync(identity, cancellationToken);
        var decision = HushVotingLicenceTransitionDecisionCore.Decide(
            catalogue, transaction.Payload, currentState);
        if (!decision.IsValid)
        {
            return ContentValidationResult.Invalid(
                decision.ValidationCode ?? HushVotingLicenceValidationCodes.TransitionNotHigher,
                decision.Message ?? "Licence transition is not valid.");
        }

        return ContentValidationResult.Valid(decision);
    }
}

/// <summary>Composite licence transaction validator contract.</summary>
public interface IHushVotingLicenceTransactionValidator
{
    bool CanValidate(Guid transactionKind);

    Task<ContentValidationResult> ValidateAsync(
        SignedTransaction<HushVotingLicenceAssignmentPayload> transaction,
        CancellationToken cancellationToken);
}
