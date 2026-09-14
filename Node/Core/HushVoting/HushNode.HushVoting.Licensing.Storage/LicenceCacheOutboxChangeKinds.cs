namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Closed v1 vocabulary for <see cref="LicenceCacheOutboxEntity.ChangeKind"/>. Values are bounded,
/// stored verbatim, and used only for diagnostics/telemetry; dispatchers always reload the latest
/// authoritative projection rather than interpreting payload history.
/// </summary>
public static class LicenceCacheOutboxChangeKinds
{
    public const string ProvisionedDefault = "provisioned_default";
    public const string ProvisionedMigrationDefault = "provisioned_migration_default";
    public const string ActivatedHigherPlan = "activated_higher_plan";
    public const string ExpiredToDefault = "expired_to_default";

    public const int MaxLength = 40;

    private static readonly IReadOnlySet<string> All =
        new HashSet<string>(StringComparer.Ordinal)
        {
            ProvisionedDefault,
            ProvisionedMigrationDefault,
            ActivatedHigherPlan,
            ExpiredToDefault,
        };

    /// <summary>Validates a bounded change kind. Returns a stable error code when invalid.</summary>
    public static bool TryValidate(string? value, out string? stableErrorCode)
    {
        stableErrorCode = null;
        if (string.IsNullOrWhiteSpace(value) || value.Length > MaxLength)
        {
            stableErrorCode = "cache_outbox_invalid_change_kind";
            return false;
        }

        if (!All.Contains(value))
        {
            stableErrorCode = "cache_outbox_unknown_change_kind";
            return false;
        }

        return true;
    }
}
