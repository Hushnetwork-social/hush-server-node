namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Immutable description of the licence release the running server is configured to
/// serve. Sourced by the host composition from the FEAT-012 immutable snapshot and
/// release digest metadata; never an editable catalogue copy.
/// </summary>
public sealed record LicenceReleaseInstallSpec(
    string CatalogueVersion,
    string ReleaseDigestSha256,
    string SchemaVersion,
    string ServerRelease,
    string ServerHost);

public enum LicenceLedgerReconcileOutcome
{
    /// <summary>The configured release is already the current release; nothing changed.</summary>
    NoChange,

    /// <summary>The configured release was appended and made current (older releases retained).</summary>
    AppendedConfiguredAsCurrent
}

/// <summary>Result of a catalogue-ledger readiness reconciliation.</summary>
public sealed record LicenceLedgerReadinessState(
    bool Ready,
    LicenceLedgerReconcileOutcome Outcome,
    string? StableFailureCode,
    string? FailureReason,
    long? RolloutWatermarkBlockHeight)
{
    public static LicenceLedgerReadinessState Ok(
        LicenceLedgerReconcileOutcome outcome,
        long? rolloutWatermarkBlockHeight) =>
        new(true, outcome, null, null, rolloutWatermarkBlockHeight);

    public static LicenceLedgerReadinessState Fail(string stableCode, string safeReason) =>
        new(false, LicenceLedgerReconcileOutcome.NoChange, stableCode, safeReason, null);
}
