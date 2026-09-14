namespace HushShared.HushVoting.Licensing.Model;

/// <summary>
/// Stable, culture-independent validation codes for the HushVoting licence catalogue domain.
/// These codes are the wire contract consumed by FEAT-013 through FEAT-018 and by startup
/// readiness diagnostics. They are immutable release data, never localised and never reordered.
/// </summary>
public static class HushVotingLicenceValidationCodes
{
    /// <summary>Required manifest/schema/release artifact is absent.</summary>
    public const string LicCatFileMissing = "LIC_CAT_FILE_MISSING";

    /// <summary>JSON violates the exact schema or bounds.</summary>
    public const string LicCatSchemaInvalid = "LIC_CAT_SCHEMA_INVALID";

    /// <summary>Configured, manifest, schema, or required version disagrees.</summary>
    public const string LicCatVersionMismatch = "LIC_CAT_VERSION_MISMATCH";

    /// <summary>Release digest does not match normalized content.</summary>
    public const string LicCatDigestMismatch = "LIC_CAT_DIGEST_MISMATCH";

    /// <summary>Required plan missing, duplicated, unknown in exact v1, or forbidden Direct placeholder present.</summary>
    public const string LicCatPlanSetInvalid = "LIC_CAT_PLAN_SET_INVALID";

    /// <summary>Direct Free is not the single enabled default.</summary>
    public const string LicCatDefaultInvalid = "LIC_CAT_DEFAULT_INVALID";

    /// <summary>Rank is duplicate, changed, negative, or inconsistent with v1 ordering.</summary>
    public const string LicCatRankInvalid = "LIC_CAT_RANK_INVALID";

    /// <summary>Perpetual/annual/unavailable term violates the accepted plan policy.</summary>
    public const string LicCatTermInvalid = "LIC_CAT_TERM_INVALID";

    /// <summary>Cap, unlimited-election flag, or Enterprise absence violates the exact plan policy.</summary>
    public const string LicCatLimitInvalid = "LIC_CAT_LIMIT_INVALID";

    /// <summary>Trustee options are missing, duplicated, non-cumulative, or allowed for Direct/Enterprise incorrectly.</summary>
    public const string LicCatGovernanceInvalid = "LIC_CAT_GOVERNANCE_INVALID";

    /// <summary>Required admin/DKG runtime profile is absent.</summary>
    public const string LicCatProfileMissing = "LIC_CAT_PROFILE_MISSING";

    /// <summary>Binding/dev flag, trustee count, threshold, or provider profile metadata disagrees.</summary>
    public const string LicCatProfileMismatch = "LIC_CAT_PROFILE_MISMATCH";

    /// <summary>Forbidden price/payment/internal/legal/readiness fields or unsafe copy appear.</summary>
    public const string LicCatCopyUnsafe = "LIC_CAT_COPY_UNSAFE";
}

/// <summary>
/// One deterministic, bounded validation failure with a stable code and a safe field path.
/// Expected validation failures are returned as data; they are never exceptions.
/// </summary>
public sealed record HushVotingLicenceValidationFailure(
    string Code,
    string FieldPath,
    string Message)
{
    /// <summary>Stable ordinal ordering for deterministic output (code, then field path, then message).</summary>
    public int CompareTo(HushVotingLicenceValidationFailure other)
    {
        var byCode = string.CompareOrdinal(Code, other.Code);
        return byCode != 0 ? byCode : string.CompareOrdinal(FieldPath, other.FieldPath);
    }
}

/// <summary>
/// Complete, stable validation-result collection. Never first-error, never partial: a catalogue
/// candidate is valid only when <see cref="IsValid"/> is true and the error list is empty.
/// </summary>
public sealed class HushVotingLicenceCatalogueValidationResult
{
    public static readonly HushVotingLicenceCatalogueValidationResult Valid = new(
        Array.Empty<HushVotingLicenceValidationFailure>());

    private readonly IReadOnlyList<HushVotingLicenceValidationFailure> _failures;

    private HushVotingLicenceCatalogueValidationResult(
        IReadOnlyList<HushVotingLicenceValidationFailure> failures)
    {
        _failures = failures;
    }

    public bool IsValid => _failures.Count == 0;

    public bool HasErrors => _failures.Count > 0;

    /// <summary>Immutable snapshot of the accumulated failures in deterministic stable order.</summary>
    public IReadOnlyList<HushVotingLicenceValidationFailure> Failures => _failures;

    public static HushVotingLicenceCatalogueValidationResult FromFailures(
        IEnumerable<HushVotingLicenceValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        var ordered = failures
            .Where(static f => f is not null)
            .OrderBy(static f => f, Comparer<HushVotingLicenceValidationFailure>.Create(
                static (a, b) => a!.CompareTo(b!)))
            .ToArray();

        return ordered.Length == 0 ? Valid : new HushVotingLicenceCatalogueValidationResult(ordered);
    }

    public static HushVotingLicenceCatalogueValidationResult Single(
        string code,
        string fieldPath,
        string message) =>
        FromFailures([new HushVotingLicenceValidationFailure(code, fieldPath, message)]);
}
