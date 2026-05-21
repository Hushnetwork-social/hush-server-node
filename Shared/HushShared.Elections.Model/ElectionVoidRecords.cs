using System.Text.RegularExpressions;

namespace HushShared.Elections.Model;

public record ElectionVoidEvidenceReferenceRecord(
    Guid Id,
    ElectionVoidEvidenceReferenceKind ReferenceKind,
    string ReferenceId,
    Guid? InternalRecordId,
    string? ExternalReference,
    string? ReferenceHash,
    ElectionVoidEvidenceVisibility Visibility,
    DateTime RecordedAt)
{
    public string ReferenceId { get; init; } =
        NormalizeRequiredValue(ReferenceId, nameof(ReferenceId));

    public string? ExternalReference { get; init; } =
        NormalizeOptionalValue(ExternalReference);

    public string? ReferenceHash { get; init; } =
        NormalizeOptionalValue(ReferenceHash);

    public Guid? InternalRecordId { get; init; } =
        ValidateInternalRecordId(ReferenceKind, InternalRecordId);

    public bool IsInternal => ReferenceKind != ElectionVoidEvidenceReferenceKind.ExternalGovernance;

    private static Guid? ValidateInternalRecordId(
        ElectionVoidEvidenceReferenceKind referenceKind,
        Guid? internalRecordId)
    {
        if (referenceKind != ElectionVoidEvidenceReferenceKind.ExternalGovernance &&
            internalRecordId is null)
        {
            throw new ArgumentException(
                "Known internal void evidence references must include an internal record id.",
                nameof(InternalRecordId));
        }

        return internalRecordId;
    }

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionVoidDecisionRecord(
    Guid Id,
    ElectionId ElectionId,
    string ActorPublicAddress,
    string ActorRole,
    Guid? SourceTransactionId,
    long? SourceBlockHeight,
    Guid? SourceBlockId,
    DateTime DecidedAt,
    ElectionLifecycleState PreviousLifecycleState,
    ElectionLifecycleState ResultingLifecycleState,
    string PublicJustification,
    byte[] PublicJustificationHash,
    IReadOnlyList<ElectionVoidEvidenceReferenceRecord> EvidenceReferences,
    Guid VoidBoundaryArtifactId,
    Guid? CurrentPublicationAttemptId,
    ElectionVoidPublicationAttemptStatus PublicationStatus)
{
    public const string ElectionOwnerRole = "ElectionOwner";

    public string ActorPublicAddress { get; init; } =
        NormalizeRequiredValue(ActorPublicAddress, nameof(ActorPublicAddress));

    public string ActorRole { get; init; } =
        NormalizeElectionOwnerRole(ActorRole);

    public ElectionLifecycleState PreviousLifecycleState { get; init; } =
        ValidatePreviousLifecycleState(PreviousLifecycleState);

    public ElectionLifecycleState ResultingLifecycleState { get; init; } =
        ValidateResultingLifecycleState(ResultingLifecycleState);

    public string PublicJustification { get; init; } =
        ElectionVoidPublicJustificationValidator.NormalizeAndThrow(PublicJustification);

    public byte[] PublicJustificationHash { get; init; } =
        CloneRequiredBytes(PublicJustificationHash, nameof(PublicJustificationHash));

    public IReadOnlyList<ElectionVoidEvidenceReferenceRecord> EvidenceReferences { get; init; } =
        EvidenceReferences?.ToArray() ?? Array.Empty<ElectionVoidEvidenceReferenceRecord>();

    private static string NormalizeElectionOwnerRole(string actorRole)
    {
        var normalized = NormalizeRequiredValue(actorRole, nameof(ActorRole));
        if (!string.Equals(normalized, ElectionOwnerRole, StringComparison.Ordinal))
        {
            throw new ArgumentException("Void decisions must be recorded with the ElectionOwner role.", nameof(ActorRole));
        }

        return normalized;
    }

    private static ElectionLifecycleState ValidateResultingLifecycleState(ElectionLifecycleState resultingLifecycleState)
    {
        if (resultingLifecycleState != ElectionLifecycleState.Voided)
        {
            throw new ArgumentException("Void decisions must result in the Voided lifecycle state.", nameof(ResultingLifecycleState));
        }

        return resultingLifecycleState;
    }

    private static ElectionLifecycleState ValidatePreviousLifecycleState(ElectionLifecycleState previousLifecycleState)
    {
        if (previousLifecycleState is ElectionLifecycleState.Finalized or ElectionLifecycleState.Voided)
        {
            throw new ArgumentException("Only draft, open, or closed elections can be voided.", nameof(PreviousLifecycleState));
        }

        return previousLifecycleState;
    }

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static byte[] CloneRequiredBytes(byte[]? value, string paramName)
    {
        if (value is not { Length: > 0 })
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.ToArray();
    }
}

public record ElectionVoidPublicationAttemptRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid VoidDecisionId,
    int AttemptNumber,
    Guid? PreviousAttemptId,
    Guid? ReportPackageId,
    ElectionVoidPublicationAttemptStatus Status,
    byte[] FrozenEvidenceHash,
    string FrozenEvidenceFingerprint,
    byte[]? PackageHash,
    int ArtifactCount,
    string? FailureCode,
    string? FailureReason,
    string? PublicStatusArtifactRef,
    string? VoidPackageArtifactRef,
    DateTime AttemptedAt,
    DateTime? SealedAt,
    string AttemptedByPublicAddress)
{
    public byte[] FrozenEvidenceHash { get; init; } =
        CloneRequiredBytes(FrozenEvidenceHash, nameof(FrozenEvidenceHash));

    public string FrozenEvidenceFingerprint { get; init; } =
        NormalizeRequiredValue(FrozenEvidenceFingerprint, nameof(FrozenEvidenceFingerprint));

    public byte[]? PackageHash { get; init; } =
        PackageHash is null ? null : PackageHash.ToArray();

    public string? FailureCode { get; init; } =
        NormalizeOptionalValue(FailureCode);

    public string? FailureReason { get; init; } =
        NormalizeOptionalValue(FailureReason);

    public string? PublicStatusArtifactRef { get; init; } =
        NormalizeOptionalValue(PublicStatusArtifactRef);

    public string? VoidPackageArtifactRef { get; init; } =
        NormalizeOptionalValue(VoidPackageArtifactRef);

    public string AttemptedByPublicAddress { get; init; } =
        NormalizeRequiredValue(AttemptedByPublicAddress, nameof(AttemptedByPublicAddress));

    public int AttemptNumber { get; init; } =
        AttemptNumber >= 1
            ? AttemptNumber
            : throw new ArgumentOutOfRangeException(nameof(AttemptNumber), "Attempt number must be at least 1.");

    public ElectionVoidPublicationAttemptStatus Status { get; init; } =
        ValidatePublicationAttemptStatus(Status, PackageHash, FailureCode, FailureReason);

    private static ElectionVoidPublicationAttemptStatus ValidatePublicationAttemptStatus(
        ElectionVoidPublicationAttemptStatus status,
        byte[]? packageHash,
        string? failureCode,
        string? failureReason)
    {
        if (status == ElectionVoidPublicationAttemptStatus.Sealed && packageHash is not { Length: > 0 })
        {
            throw new ArgumentException("Sealed void publication attempts require a package hash.", nameof(PackageHash));
        }

        if (status == ElectionVoidPublicationAttemptStatus.GenerationFailed &&
            (string.IsNullOrWhiteSpace(failureCode) || string.IsNullOrWhiteSpace(failureReason)))
        {
            throw new ArgumentException("Failed void publication attempts require a failure code and reason.");
        }

        return status;
    }

    private static byte[] CloneRequiredBytes(byte[]? value, string paramName)
    {
        if (value is not { Length: > 0 })
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.ToArray();
    }

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionVoidSupersededArtifactRecord(
    Guid Id,
    ElectionId ElectionId,
    Guid VoidDecisionId,
    ElectionVoidSupersededArtifactKind ArtifactKind,
    Guid? ReportPackageId,
    Guid? ReportArtifactId,
    string ArtifactRef,
    string? ArtifactHash,
    DateTime SupersededAt)
{
    public string ArtifactRef { get; init; } =
        NormalizeRequiredValue(ArtifactRef, nameof(ArtifactRef));

    public string? ArtifactHash { get; init; } =
        NormalizeOptionalValue(ArtifactHash);

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }

    private static string? NormalizeOptionalValue(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record ElectionVoidPublicStatusRecord(
    ElectionId ElectionId,
    Guid VoidDecisionId,
    Guid PublicationAttemptId,
    string Status,
    string PublicJustification,
    string VerifierResultCode,
    string VoidPackageArtifactRef,
    string VoidPackageHash,
    DateTime PublishedAt,
    IReadOnlyList<ElectionVoidSupersededPublicArtifactReference> SupersededArtifacts)
{
    public string Status { get; init; } =
        NormalizeExactStatus(Status);

    public string PublicJustification { get; init; } =
        ElectionVoidPublicJustificationValidator.NormalizeAndThrow(PublicJustification);

    public string VerifierResultCode { get; init; } =
        NormalizeVoidVerifierResultCode(VerifierResultCode);

    public string VoidPackageArtifactRef { get; init; } =
        NormalizeRequiredValue(VoidPackageArtifactRef, nameof(VoidPackageArtifactRef));

    public string VoidPackageHash { get; init; } =
        NormalizeRequiredValue(VoidPackageHash, nameof(VoidPackageHash));

    public IReadOnlyList<ElectionVoidSupersededPublicArtifactReference> SupersededArtifacts { get; init; } =
        SupersededArtifacts?.ToArray() ?? Array.Empty<ElectionVoidSupersededPublicArtifactReference>();

    private static string NormalizeVoidVerifierResultCode(string verifierResultCode)
    {
        var normalized = NormalizeRequiredValue(verifierResultCode, nameof(VerifierResultCode));
        if (!string.Equals(normalized, "election_voided", StringComparison.Ordinal))
        {
            throw new ArgumentException("Void public status must use the election_voided verifier result.", nameof(VerifierResultCode));
        }

        return normalized;
    }

    private static string NormalizeExactStatus(string value)
    {
        var normalized = NormalizeRequiredValue(value, nameof(Status));
        if (!string.Equals(normalized, "VOID", StringComparison.Ordinal))
        {
            throw new ArgumentException("Void public status must use the exact marker VOID.", nameof(Status));
        }

        return normalized;
    }

    private static string NormalizeRequiredValue(string value, string paramName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Value is required.", paramName);
        }

        return value.Trim();
    }
}

public record ElectionVoidSupersededPublicArtifactReference(
    ElectionVoidSupersededArtifactKind ArtifactKind,
    string ArtifactRef,
    string? ArtifactHash)
{
    public string ArtifactRef { get; init; } =
        string.IsNullOrWhiteSpace(ArtifactRef)
            ? throw new ArgumentException("Value is required.", nameof(ArtifactRef))
            : ArtifactRef.Trim();

    public string? ArtifactHash { get; init; } =
        string.IsNullOrWhiteSpace(ArtifactHash) ? null : ArtifactHash.Trim();
}

public record ElectionVoidRestrictedEvidenceIndexRecord(
    ElectionId ElectionId,
    Guid VoidDecisionId,
    Guid PublicationAttemptId,
    IReadOnlyList<ElectionVoidEvidenceReferenceRecord> EvidenceReferences,
    Guid? HistoricalUnofficialResultArtifactId,
    string? HistoricalUnofficialResultHash,
    DateTime RecordedAt)
{
    public IReadOnlyList<ElectionVoidEvidenceReferenceRecord> EvidenceReferences { get; init; } =
        EvidenceReferences?.ToArray() ?? Array.Empty<ElectionVoidEvidenceReferenceRecord>();

    public string? HistoricalUnofficialResultHash { get; init; } =
        string.IsNullOrWhiteSpace(HistoricalUnofficialResultHash) ? null : HistoricalUnofficialResultHash.Trim();
}

public sealed record ElectionVoidJustificationValidationResult(
    bool IsValid,
    string NormalizedJustification,
    IReadOnlyList<string> Errors);

public static partial class ElectionVoidPublicJustificationValidator
{
    public const int MinLength = 10;
    public const int MaxLength = 1000;

    private static readonly Regex EmailPattern = new(
        @"[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,}",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex PhonePattern = new(
        @"(?<!\d)\+?\d[\d\s().\-]{7,}\d(?!\d)",
        RegexOptions.Compiled);

    private static readonly string[] ForbiddenFragments =
    [
        "private key",
        "begin private key",
        "kms:",
        "aws:kms",
        "kms alias",
        "password",
        "secret access key",
        "support log",
        "raw log",
        "voter identity",
        "vote choice",
    ];

    public static ElectionVoidJustificationValidationResult Validate(string? justification)
    {
        var normalized = Normalize(justification);
        var errors = new List<string>();

        if (normalized.Length < MinLength)
        {
            errors.Add(ElectionVoidValidationCodes.JustificationTooShort);
        }

        if (normalized.Length > MaxLength)
        {
            errors.Add(ElectionVoidValidationCodes.JustificationTooLong);
        }

        if (ForbiddenFragments.Any(fragment =>
                normalized.Contains(fragment, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add(ElectionVoidValidationCodes.JustificationContainsRestrictedMaterial);
        }

        if (EmailPattern.IsMatch(normalized) || PhonePattern.IsMatch(normalized))
        {
            errors.Add(ElectionVoidValidationCodes.JustificationContainsPersonalData);
        }

        return new ElectionVoidJustificationValidationResult(
            errors.Count == 0,
            normalized,
            errors);
    }

    public static string NormalizeAndThrow(string? justification)
    {
        var result = Validate(justification);
        if (!result.IsValid)
        {
            throw new ArgumentException(
                $"Void justification is invalid: {string.Join(", ", result.Errors)}",
                nameof(justification));
        }

        return result.NormalizedJustification;
    }

    private static string Normalize(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public static class ElectionVoidValidationCodes
{
    public const string JustificationTooShort = "void_justification_too_short";
    public const string JustificationTooLong = "void_justification_too_long";
    public const string JustificationContainsRestrictedMaterial = "void_justification_contains_restricted_material";
    public const string JustificationContainsPersonalData = "void_justification_contains_personal_data";
    public const string InternalEvidenceReferenceMissingRecordId = "void_internal_evidence_reference_missing_record_id";
    public const string EvidenceReferenceMissingId = "void_evidence_reference_missing_id";
}

public static class ElectionVoidEvidenceReferenceValidator
{
    public static IReadOnlyList<string> Validate(IReadOnlyList<ElectionVoidEvidenceReferenceRecord>? references)
    {
        if (references is null || references.Count == 0)
        {
            return Array.Empty<string>();
        }

        var errors = new List<string>();
        foreach (var reference in references)
        {
            if (string.IsNullOrWhiteSpace(reference.ReferenceId))
            {
                errors.Add(ElectionVoidValidationCodes.EvidenceReferenceMissingId);
            }

            if (reference.IsInternal && reference.InternalRecordId is null)
            {
                errors.Add(ElectionVoidValidationCodes.InternalEvidenceReferenceMissingRecordId);
            }
        }

        return errors.Distinct(StringComparer.Ordinal).ToArray();
    }
}
