using HushShared.Elections.Model;

namespace HushNode.Elections;

public enum AdminOnlyProtectedTallyCustodyReconciliationRunMode
{
    DryRun = 0,
    Repair = 1,
}

public enum AdminOnlyProtectedTallyCustodyReconciliationSeverity
{
    Info = 0,
    Warning = 1,
    High = 2,
    Critical = 3,
}

public static class AdminOnlyProtectedTallyCustodyReconciliationConditionCodes
{
    public const string Accepted = "custody_reconciliation_accepted";
    public const string OrphanedProviderKey = "orphaned_provider_key";
    public const string ProviderKeyMissing = "provider_key_missing";
    public const string ProviderMetadataMismatch = "provider_metadata_mismatch";
    public const string ProviderInventoryError = "provider_inventory_error";
    public const string StaleCustodyRow = "stale_custody_row";
    public const string OpenCustodyIncomplete = "open_custody_incomplete";
    public const string FinalizedScalarNotDestroyed = "finalized_scalar_not_destroyed";
    public const string FinalizedKeyStillEnabled = "finalized_key_still_enabled";
    public const string DeletionNotScheduled = "deletion_not_scheduled";
    public const string DeletionScheduleDrift = "deletion_schedule_drift";
    public const string RetryRequired = "retry_required";
    public const string ExceptionRequired = "exception_required";
}

public sealed record AdminOnlyProtectedTallyCustodyProviderKeyObservation(
    string PublicCustodyReferenceHash,
    string? KmsKeyId,
    string? KmsKeyArn,
    string? KmsAlias,
    string? KmsRegion,
    string? KmsAccountBoundary,
    bool Exists,
    bool Enabled,
    bool AliasMatches,
    bool TagsMatch,
    bool DeletionScheduled,
    DateTime? DeletionDate,
    string? ProviderErrorCode = null,
    string? ProviderErrorMessage = null)
{
    public string PublicCustodyReferenceHash { get; init; } = string.IsNullOrWhiteSpace(PublicCustodyReferenceHash)
        ? "unknown-public-custody-reference"
        : PublicCustodyReferenceHash.Trim();
    public string? KmsKeyId { get; init; } = Normalize(KmsKeyId);
    public string? KmsKeyArn { get; init; } = Normalize(KmsKeyArn);
    public string? KmsAlias { get; init; } = Normalize(KmsAlias);
    public string? KmsRegion { get; init; } = Normalize(KmsRegion);
    public string? KmsAccountBoundary { get; init; } = Normalize(KmsAccountBoundary);
    public string? ProviderErrorCode { get; init; } = Normalize(ProviderErrorCode);
    public string? ProviderErrorMessage { get; init; } = Normalize(ProviderErrorMessage);

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record AdminOnlyProtectedTallyCustodyReconciliationRequest(
    AdminOnlyProtectedTallyCustodyReconciliationRunMode RunMode,
    IReadOnlyList<ElectionAdminOnlyProtectedTallyEnvelopeRecord> CustodyRows,
    IReadOnlyList<ElectionRecord> Elections,
    IReadOnlyList<AdminOnlyProtectedTallyCustodyProviderKeyObservation> ProviderKeys,
    string EnvironmentName,
    string ProviderProfile,
    string OperatorServiceIdentity,
    DateTime StartedAt,
    int MaximumDeletionWindowDays = 7)
{
    public IReadOnlyList<ElectionAdminOnlyProtectedTallyEnvelopeRecord> CustodyRows { get; init; } =
        CustodyRows ?? Array.Empty<ElectionAdminOnlyProtectedTallyEnvelopeRecord>();
    public IReadOnlyList<ElectionRecord> Elections { get; init; } = Elections ?? Array.Empty<ElectionRecord>();
    public IReadOnlyList<AdminOnlyProtectedTallyCustodyProviderKeyObservation> ProviderKeys { get; init; } =
        ProviderKeys ?? Array.Empty<AdminOnlyProtectedTallyCustodyProviderKeyObservation>();
    public string EnvironmentName { get; init; } = string.IsNullOrWhiteSpace(EnvironmentName)
        ? "unspecified-environment"
        : EnvironmentName.Trim();
    public string ProviderProfile { get; init; } = string.IsNullOrWhiteSpace(ProviderProfile)
        ? "unspecified-provider-profile"
        : ProviderProfile.Trim();
    public string OperatorServiceIdentity { get; init; } = string.IsNullOrWhiteSpace(OperatorServiceIdentity)
        ? "unspecified-operator"
        : OperatorServiceIdentity.Trim();
    public int MaximumDeletionWindowDays { get; init; } = MaximumDeletionWindowDays <= 0 ? 7 : MaximumDeletionWindowDays;
}

public sealed record AdminOnlyProtectedTallyCustodyReconciliationItem(
    ElectionId? ElectionId,
    string SelectedProfileId,
    string PublicCustodyReferenceHash,
    string PrivateCustodyRowReference,
    string ConditionCode,
    AdminOnlyProtectedTallyCustodyReconciliationSeverity Severity,
    string ProposedAction,
    string RepairResult,
    ElectionAdminOnlyProtectedTallyCustodyLifecycleState? StateBefore,
    ElectionAdminOnlyProtectedTallyCustodyLifecycleState? StateAfter,
    DateTime? NextRetryAt,
    string? UnresolvedExceptionId,
    ElectionAdminOnlyProtectedTallyCustodyRestrictedEvidence? RestrictedEvidence,
    ElectionAdminOnlyProtectedTallyEnvelopeRecord? EnvelopeToPersist);

public sealed record AdminOnlyProtectedTallyCustodyReconciliationSummary(
    int AffectedElectionCount,
    int ItemCount,
    int CriticalCount,
    int HighCount,
    int WarningCount,
    int InfoCount,
    bool HasUnresolvedExceptions,
    bool BlocksReadinessGate,
    string ReadinessGateId,
    string DimensionId);

public sealed record AdminOnlyProtectedTallyCustodyReconciliationReport(
    Guid RunId,
    AdminOnlyProtectedTallyCustodyReconciliationRunMode RunMode,
    DateTime StartedAt,
    DateTime FinishedAt,
    string EnvironmentName,
    string ProviderProfile,
    string OperatorServiceIdentity,
    AdminOnlyProtectedTallyCustodyReconciliationSummary Summary,
    IReadOnlyList<AdminOnlyProtectedTallyCustodyReconciliationItem> Items,
    ElectionAdminOnlyProtectedTallyCustodyReadinessFragment ReadinessFragment);

public static class AdminOnlyProtectedTallyCustodyReconciliationEngine
{
    public static AdminOnlyProtectedTallyCustodyReconciliationReport Run(
        AdminOnlyProtectedTallyCustodyReconciliationRequest request,
        IAdminOnlyProtectedTallyCustodyLifecycleAuthority lifecycleAuthority)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(lifecycleAuthority);

        var finishedAt = DateTime.UtcNow;
        var rowsByPublicRef = request.CustodyRows
            .Where(x => !string.IsNullOrWhiteSpace(x.PublicCustodyReferenceHash))
            .GroupBy(x => x.PublicCustodyReferenceHash!, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var rowsByKeyOrAlias = BuildRowLookupByKeyOrAlias(request.CustodyRows);
        var electionLookup = request.Elections.ToDictionary(x => x.ElectionId);
        var providerByPublicRef = request.ProviderKeys
            .Where(x => !string.IsNullOrWhiteSpace(x.PublicCustodyReferenceHash))
            .GroupBy(x => x.PublicCustodyReferenceHash, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.First(), StringComparer.Ordinal);
        var providerByKeyOrAlias = BuildProviderLookupByKeyOrAlias(request.ProviderKeys);
        var items = new List<AdminOnlyProtectedTallyCustodyReconciliationItem>();

        foreach (var providerKey in request.ProviderKeys)
        {
            if (!rowsByPublicRef.ContainsKey(providerKey.PublicCustodyReferenceHash) &&
                !MatchesRowByKeyOrAlias(providerKey, rowsByKeyOrAlias))
            {
                items.Add(BuildProviderOnlyItem(providerKey, request.RunMode));
            }
        }

        foreach (var row in request.CustodyRows)
        {
            var providerKey = ResolveProviderObservation(row, providerByPublicRef, providerByKeyOrAlias);
            electionLookup.TryGetValue(row.ElectionId, out var election);
            AddRowItems(items, request, lifecycleAuthority, row, election, providerKey);
        }

        if (items.Count == 0)
        {
            items.Add(BuildAcceptedItem(request));
        }

        var summary = BuildSummary(items);
        var readinessFragment = BuildReadinessFragment(request, items, summary, finishedAt);
        return new AdminOnlyProtectedTallyCustodyReconciliationReport(
            Guid.NewGuid(),
            request.RunMode,
            request.StartedAt,
            finishedAt,
            request.EnvironmentName,
            request.ProviderProfile,
            request.OperatorServiceIdentity,
            summary,
            items,
            readinessFragment);
    }

    private static void AddRowItems(
        List<AdminOnlyProtectedTallyCustodyReconciliationItem> items,
        AdminOnlyProtectedTallyCustodyReconciliationRequest request,
        IAdminOnlyProtectedTallyCustodyLifecycleAuthority lifecycleAuthority,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        ElectionRecord? election,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation? providerKey)
    {
        var lifecycleState = row.ResolveCustodyLifecycleState();
        if (providerKey is not null)
        {
            AddProviderDriftItems(items, request, row, providerKey);
        }
        else if (!string.IsNullOrWhiteSpace(row.KmsKeyId) || !string.IsNullOrWhiteSpace(row.KmsAlias))
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderKeyMissing,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Review provider inventory; key, alias, or tags were not observed.",
                "not_attempted_provider_missing",
                providerKey: null,
                envelopeToPersist: null));
        }

        if (election is null)
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.StaleCustodyRow,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Resolve the private custody row whose election record was not observed.",
                "manual_operator_action_required",
                providerKey,
                envelopeToPersist: null));
        }
        else if (election.LifecycleState == ElectionLifecycleState.Draft &&
                 lifecycleState is not ElectionAdminOnlyProtectedTallyCustodyLifecycleState.NotRequired
                     and not ElectionAdminOnlyProtectedTallyCustodyLifecycleState.LegacyStaticKms)
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.StaleCustodyRow,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Resolve custody that exists for an election that has not opened.",
                "manual_operator_action_required",
                providerKey,
                envelopeToPersist: null));
        }

        if (election?.LifecycleState == ElectionLifecycleState.Finalized && !HasDestroyedMarker(row))
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.FinalizedScalarNotDestroyed,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.Critical,
                "Destroy the persisted sealed scalar before readiness evidence can be accepted.",
                "manual_operator_action_required",
                providerKey,
                envelopeToPersist: null));
        }

        switch (lifecycleState)
        {
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ProviderUnavailable:
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.KeyCreated:
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.SealedScalarPersisted:
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.OpenReady:
                items.Add(BuildRowItem(
                    row,
                    AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.OpenCustodyIncomplete,
                    AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                    "Resolve incomplete open custody state or mark an explicit exception.",
                    "manual_operator_action_required",
                    providerKey,
                    envelopeToPersist: null));
                break;
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ScalarDestroyed:
                items.Add(BuildRepairableFinalizationItem(
                    request,
                    lifecycleAuthority,
                    row,
                    providerKey,
                    AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.FinalizedKeyStillEnabled,
                    "Disable the per-election key and schedule deletion."));
                break;
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.KeyDisabled:
                items.Add(BuildRepairableFinalizationItem(
                    request,
                    lifecycleAuthority,
                    row,
                    providerKey,
                    AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.DeletionNotScheduled,
                    "Schedule deletion for the disabled per-election key."));
                break;
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.RetryRequired:
                items.Add(BuildRepairableFinalizationItem(
                    request,
                    lifecycleAuthority,
                    row,
                    providerKey,
                    AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.RetryRequired,
                    "Retry the last allowed custody lifecycle action."));
                break;
            case ElectionAdminOnlyProtectedTallyCustodyLifecycleState.ExceptionRequired:
                items.Add(BuildRowItem(
                    row,
                    AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ExceptionRequired,
                    AdminOnlyProtectedTallyCustodyReconciliationSeverity.Critical,
                    "Operator exception review is required before readiness evidence can be accepted.",
                    "manual_exception_required",
                    providerKey,
                    envelopeToPersist: null));
                break;
        }
    }

    private static void AddProviderDriftItems(
        List<AdminOnlyProtectedTallyCustodyReconciliationItem> items,
        AdminOnlyProtectedTallyCustodyReconciliationRequest request,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation providerKey)
    {
        if (!string.IsNullOrWhiteSpace(providerKey.ProviderErrorCode))
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderInventoryError,
                IsPermissionError(providerKey.ProviderErrorCode)
                    ? AdminOnlyProtectedTallyCustodyReconciliationSeverity.Critical
                    : AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Restore IAM/KMS inventory permissions or record an explicit operator exception.",
                ResolveNoRepairResult(request.RunMode),
                providerKey,
                envelopeToPersist: null));
        }

        if (!providerKey.Exists)
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderKeyMissing,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Review provider inventory; key was not observed.",
                ResolveNoRepairResult(request.RunMode),
                providerKey,
                envelopeToPersist: null));
        }

        if (providerKey.Exists &&
            (!providerKey.AliasMatches || !providerKey.TagsMatch || !PublicReferenceMatches(row, providerKey)))
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ProviderMetadataMismatch,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Repair or explicitly except mismatched KMS alias, tags, or public reference.",
                ResolveNoRepairResult(request.RunMode),
                providerKey,
                envelopeToPersist: null));
        }

        if (row.ResolveCustodyLifecycleState() == ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled &&
            (!providerKey.DeletionScheduled ||
             !DeletionDateMatches(row, providerKey) ||
             DeletionWindowOutsidePolicy(row, providerKey, request.MaximumDeletionWindowDays)))
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.DeletionScheduleDrift,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Repair deletion schedule drift, retention-window drift, or record an explicit exception.",
                ResolveNoRepairResult(request.RunMode),
                providerKey,
                envelopeToPersist: null));
        }

        if (HasDestroyedMarker(row) &&
            providerKey.Enabled &&
            row.ResolveCustodyLifecycleState() == ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled)
        {
            items.Add(BuildRowItem(
                row,
                AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.FinalizedKeyStillEnabled,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                "Disable the per-election key and schedule deletion.",
                ResolveNoRepairResult(request.RunMode),
                providerKey,
                envelopeToPersist: null));
        }
    }

    private static AdminOnlyProtectedTallyCustodyReconciliationItem BuildRepairableFinalizationItem(
        AdminOnlyProtectedTallyCustodyReconciliationRequest request,
        IAdminOnlyProtectedTallyCustodyLifecycleAuthority lifecycleAuthority,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation? providerKey,
        string conditionCode,
        string proposedAction)
    {
        if (request.RunMode == AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun)
        {
            return BuildRowItem(
                row,
                conditionCode,
                AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
                proposedAction,
                "dry_run_no_change",
                providerKey,
                envelopeToPersist: null);
        }

        var cleanup = lifecycleAuthority.BuildFinalizationCleanup(row, request.StartedAt);
        var repairedEnvelope = cleanup.Handled ? cleanup.EnvelopeToPersist : null;
        return BuildRowItem(
            row,
            conditionCode,
            string.IsNullOrWhiteSpace(cleanup.Error)
                ? AdminOnlyProtectedTallyCustodyReconciliationSeverity.Info
                : AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
            proposedAction,
            cleanup.Handled && string.IsNullOrWhiteSpace(cleanup.Error)
                ? "repair_applied"
                : "repair_retry_required",
            providerKey,
            repairedEnvelope);
    }

    private static AdminOnlyProtectedTallyCustodyReconciliationItem BuildRowItem(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        string conditionCode,
        AdminOnlyProtectedTallyCustodyReconciliationSeverity severity,
        string proposedAction,
        string repairResult,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation? providerKey,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord? envelopeToPersist)
    {
        var restrictedEvidence = new ElectionAdminOnlyProtectedTallyCustodyRestrictedEvidence(
            BuildPrivateCustodyRowReference(row.ElectionId, row.SelectedProfileId),
            row.KmsKeyId ?? providerKey?.KmsKeyId,
            row.KmsKeyArn ?? providerKey?.KmsKeyArn,
            row.KmsAlias ?? providerKey?.KmsAlias,
            row.KmsRegion ?? providerKey?.KmsRegion,
            row.KmsAccountBoundary ?? providerKey?.KmsAccountBoundary,
            string.IsNullOrWhiteSpace(row.KmsTagSetHash) ? null : $"tag-set-hash:{row.KmsTagSetHash}",
            row.CustodyActionServiceIdentity,
            row.SealedEnvelopeHash,
            row.CustodyLastErrorCode ?? providerKey?.ProviderErrorCode,
            row.CustodyLastErrorMessage ?? providerKey?.ProviderErrorMessage);

        return new AdminOnlyProtectedTallyCustodyReconciliationItem(
            row.ElectionId,
            row.SelectedProfileId,
            row.PublicCustodyReferenceHash ?? "missing-public-custody-reference",
            BuildPrivateCustodyRowReference(row.ElectionId, row.SelectedProfileId),
            conditionCode,
            severity,
            proposedAction,
            repairResult,
            row.ResolveCustodyLifecycleState(),
            envelopeToPersist?.ResolveCustodyLifecycleState(),
            envelopeToPersist?.CustodyNextRetryAt ?? row.CustodyNextRetryAt,
            envelopeToPersist?.CustodyExceptionId ?? row.CustodyExceptionId,
            restrictedEvidence,
            envelopeToPersist);
    }

    private static AdminOnlyProtectedTallyCustodyReconciliationItem BuildProviderOnlyItem(
        AdminOnlyProtectedTallyCustodyProviderKeyObservation providerKey,
        AdminOnlyProtectedTallyCustodyReconciliationRunMode runMode)
    {
        var restrictedEvidence = new ElectionAdminOnlyProtectedTallyCustodyRestrictedEvidence(
            $"provider-only/{providerKey.PublicCustodyReferenceHash}",
            providerKey.KmsKeyId,
            providerKey.KmsKeyArn,
            providerKey.KmsAlias,
            providerKey.KmsRegion,
            providerKey.KmsAccountBoundary,
            null,
            null,
            null,
            providerKey.ProviderErrorCode,
            providerKey.ProviderErrorMessage);

        return new AdminOnlyProtectedTallyCustodyReconciliationItem(
            ElectionId: null,
            SelectedProfileId: "unknown_profile",
            providerKey.PublicCustodyReferenceHash,
            $"provider-only/{providerKey.PublicCustodyReferenceHash}",
            AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.OrphanedProviderKey,
            AdminOnlyProtectedTallyCustodyReconciliationSeverity.High,
            "Create a matching private custody row, schedule provider cleanup, or record an explicit exception.",
            ResolveNoRepairResult(runMode),
            StateBefore: null,
            StateAfter: null,
            NextRetryAt: null,
            UnresolvedExceptionId: null,
            restrictedEvidence,
            EnvelopeToPersist: null);
    }

    private static AdminOnlyProtectedTallyCustodyReconciliationItem BuildAcceptedItem(
        AdminOnlyProtectedTallyCustodyReconciliationRequest request)
    {
        var row = request.CustodyRows.FirstOrDefault();
        return new AdminOnlyProtectedTallyCustodyReconciliationItem(
            row?.ElectionId,
            row?.SelectedProfileId ?? "not_election_specific",
            row?.PublicCustodyReferenceHash ?? "not_applicable",
            row is null
                ? "not_applicable"
                : BuildPrivateCustodyRowReference(row.ElectionId, row.SelectedProfileId),
            AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.Accepted,
            AdminOnlyProtectedTallyCustodyReconciliationSeverity.Info,
            "No reconciliation drift was detected.",
            "no_repair_needed",
            row?.ResolveCustodyLifecycleState(),
            row?.ResolveCustodyLifecycleState(),
            NextRetryAt: null,
            UnresolvedExceptionId: null,
            RestrictedEvidence: null,
            EnvelopeToPersist: null);
    }

    private static AdminOnlyProtectedTallyCustodyReconciliationSummary BuildSummary(
        IReadOnlyList<AdminOnlyProtectedTallyCustodyReconciliationItem> items)
    {
        var blockingItems = items
            .Where(x => x.ConditionCode != AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.Accepted)
            .ToArray();
        return new AdminOnlyProtectedTallyCustodyReconciliationSummary(
            items.Where(x => x.ElectionId is not null).Select(x => x.ElectionId).Distinct().Count(),
            items.Count,
            items.Count(x => x.Severity == AdminOnlyProtectedTallyCustodyReconciliationSeverity.Critical),
            items.Count(x => x.Severity == AdminOnlyProtectedTallyCustodyReconciliationSeverity.High),
            items.Count(x => x.Severity == AdminOnlyProtectedTallyCustodyReconciliationSeverity.Warning),
            items.Count(x => x.Severity == AdminOnlyProtectedTallyCustodyReconciliationSeverity.Info),
            blockingItems.Any(x => !string.IsNullOrWhiteSpace(x.UnresolvedExceptionId) ||
                                   x.ConditionCode == AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.ExceptionRequired),
            blockingItems.Length > 0,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId,
            ElectionAdminOnlyProtectedTallyCustodyReadinessIds.DimensionId);
    }

    private static ElectionAdminOnlyProtectedTallyCustodyReadinessFragment BuildReadinessFragment(
        AdminOnlyProtectedTallyCustodyReconciliationRequest request,
        IReadOnlyList<AdminOnlyProtectedTallyCustodyReconciliationItem> items,
        AdminOnlyProtectedTallyCustodyReconciliationSummary summary,
        DateTime recordedAt)
    {
        var election = request.Elections.FirstOrDefault() ??
            ElectionModelFactory.CreateDraftRecord(
                electionId: ElectionId.NewElectionId,
                title: "Custody reconciliation",
                shortDescription: "Custody reconciliation evidence",
                ownerPublicAddress: request.OperatorServiceIdentity,
                externalReferenceCode: "custody-reconciliation",
                electionClass: ElectionClass.OrganizationalRemoteVoting,
                bindingStatus: ElectionBindingStatus.NonBinding,
                selectedProfileId: "admin-dev-1of1",
                selectedProfileDevOnly: true,
                governanceMode: ElectionGovernanceMode.AdminOnly,
                disclosureMode: ElectionDisclosureMode.FinalResultsOnly,
                participationPrivacyMode: ParticipationPrivacyMode.PublicCheckoffAnonymousBallotPrivateChoice,
                voteUpdatePolicy: VoteUpdatePolicy.SingleSubmissionOnly,
                eligibilitySourceType: EligibilitySourceType.OrganizationImportedRoster,
                eligibilityMutationPolicy: EligibilityMutationPolicy.FrozenAtOpen,
                outcomeRule: new OutcomeRuleDefinition(
                    OutcomeRuleKind.SingleWinner,
                    "single_winner",
                    SeatCount: 1,
                    BlankVoteCountsForTurnout: true,
                    BlankVoteExcludedFromWinnerSelection: true,
                    BlankVoteExcludedFromThresholdDenominator: false,
                    TieResolutionRule: "tie_unresolved",
                    CalculationBasis: "highest_non_blank_votes"),
                approvedClientApplications: [],
                protocolOmegaVersion: "omega-v1",
                reportingPolicy: ReportingPolicy.DefaultPhaseOnePackage,
                reviewWindowPolicy: ReviewWindowPolicy.NoReviewWindow,
                ownerOptions:
                [
                    new ElectionOptionDefinition("ok", "OK", null, 1, IsBlankOption: false),
                ]);
        var envelope = request.CustodyRows.FirstOrDefault(x =>
            x.ResolveCustodyLifecycleState() == ElectionAdminOnlyProtectedTallyCustodyLifecycleState.DeletionScheduled) ??
            request.CustodyRows.FirstOrDefault();
        var fragment = ElectionAdminOnlyProtectedTallyCustodyEvidenceBuilder.BuildReconciliationEvidence(
            election,
            summary.BlocksReadinessGate ? null : envelope,
            recordedAt);

        if (!summary.BlocksReadinessGate)
        {
            return fragment with
            {
                AcceptedGateIds =
                    new[] { ElectionAdminOnlyProtectedTallyCustodyReadinessIds.ReconciliationGateId },
            };
        }

        return fragment with
        {
            AcceptedGateIds = Array.Empty<string>(),
            Exceptions = items
                .Where(x => x.ConditionCode != AdminOnlyProtectedTallyCustodyReconciliationConditionCodes.Accepted)
                .Select(x => new ElectionAdminOnlyProtectedTallyCustodyExceptionEvidence(
                    $"custody-reconciliation-{x.ConditionCode}-{Guid.NewGuid():N}",
                    ElectionAdminOnlyProtectedTallyCustodyActionKind.Reconciliation,
                    x.Severity == AdminOnlyProtectedTallyCustodyReconciliationSeverity.Critical
                        ? ElectionAdminOnlyProtectedTallyCustodyActionResult.ExceptionRequired
                        : ElectionAdminOnlyProtectedTallyCustodyActionResult.RetryRequired,
                    x.ConditionCode,
                    x.ProposedAction,
                    x.RestrictedEvidence?.ProviderErrorCode,
                    BlocksReadinessScoreIncrease: true,
                    recordedAt))
                .ToArray(),
        };
    }

    private static Dictionary<string, ElectionAdminOnlyProtectedTallyEnvelopeRecord> BuildRowLookupByKeyOrAlias(
        IEnumerable<ElectionAdminOnlyProtectedTallyEnvelopeRecord> rows)
    {
        var lookup = new Dictionary<string, ElectionAdminOnlyProtectedTallyEnvelopeRecord>(StringComparer.Ordinal);
        foreach (var row in rows)
        {
            AddLookup(lookup, row.KmsKeyId, row);
            AddLookup(lookup, row.KmsAlias, row);
        }

        return lookup;
    }

    private static Dictionary<string, AdminOnlyProtectedTallyCustodyProviderKeyObservation> BuildProviderLookupByKeyOrAlias(
        IEnumerable<AdminOnlyProtectedTallyCustodyProviderKeyObservation> providerKeys)
    {
        var lookup = new Dictionary<string, AdminOnlyProtectedTallyCustodyProviderKeyObservation>(StringComparer.Ordinal);
        foreach (var providerKey in providerKeys)
        {
            AddProviderLookup(lookup, providerKey.KmsKeyId, providerKey);
            AddProviderLookup(lookup, providerKey.KmsAlias, providerKey);
        }

        return lookup;
    }

    private static void AddLookup(
        IDictionary<string, ElectionAdminOnlyProtectedTallyEnvelopeRecord> lookup,
        string? key,
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row)
    {
        if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
        {
            lookup[key] = row;
        }
    }

    private static void AddProviderLookup(
        IDictionary<string, AdminOnlyProtectedTallyCustodyProviderKeyObservation> lookup,
        string? key,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation providerKey)
    {
        if (!string.IsNullOrWhiteSpace(key) && !lookup.ContainsKey(key))
        {
            lookup[key] = providerKey;
        }
    }

    private static AdminOnlyProtectedTallyCustodyProviderKeyObservation? ResolveProviderObservation(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        IReadOnlyDictionary<string, AdminOnlyProtectedTallyCustodyProviderKeyObservation> providerByPublicRef,
        IReadOnlyDictionary<string, AdminOnlyProtectedTallyCustodyProviderKeyObservation> providerByKeyOrAlias)
    {
        if (!string.IsNullOrWhiteSpace(row.PublicCustodyReferenceHash) &&
            providerByPublicRef.TryGetValue(row.PublicCustodyReferenceHash, out var byPublicRef))
        {
            return byPublicRef;
        }

        if (!string.IsNullOrWhiteSpace(row.KmsKeyId) &&
            providerByKeyOrAlias.TryGetValue(row.KmsKeyId, out var byKeyId))
        {
            return byKeyId;
        }

        if (!string.IsNullOrWhiteSpace(row.KmsAlias) &&
            providerByKeyOrAlias.TryGetValue(row.KmsAlias, out var byAlias))
        {
            return byAlias;
        }

        return null;
    }

    private static bool MatchesRowByKeyOrAlias(
        AdminOnlyProtectedTallyCustodyProviderKeyObservation providerKey,
        IReadOnlyDictionary<string, ElectionAdminOnlyProtectedTallyEnvelopeRecord> rowsByKeyOrAlias) =>
        (!string.IsNullOrWhiteSpace(providerKey.KmsKeyId) && rowsByKeyOrAlias.ContainsKey(providerKey.KmsKeyId)) ||
        (!string.IsNullOrWhiteSpace(providerKey.KmsAlias) && rowsByKeyOrAlias.ContainsKey(providerKey.KmsAlias));

    private static bool DeletionDateMatches(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation providerKey) =>
        row.KmsDeletionDate is null ||
        providerKey.DeletionDate is null ||
        Math.Abs((row.KmsDeletionDate.Value - providerKey.DeletionDate.Value).TotalHours) <= 24;

    private static bool PublicReferenceMatches(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation providerKey) =>
        string.IsNullOrWhiteSpace(row.PublicCustodyReferenceHash) ||
        string.Equals(
            row.PublicCustodyReferenceHash,
            providerKey.PublicCustodyReferenceHash,
            StringComparison.Ordinal);

    private static bool DeletionWindowOutsidePolicy(
        ElectionAdminOnlyProtectedTallyEnvelopeRecord row,
        AdminOnlyProtectedTallyCustodyProviderKeyObservation providerKey,
        int maximumDeletionWindowDays)
    {
        if (row.DeletionWindowDays is > 0 && row.DeletionWindowDays > maximumDeletionWindowDays)
        {
            return true;
        }

        var effectiveDeletionDate = providerKey.DeletionDate ?? row.KmsDeletionDate;
        if (row.KmsDeletionScheduledAt is null || effectiveDeletionDate is null)
        {
            return false;
        }

        var effectiveWindow = (effectiveDeletionDate.Value - row.KmsDeletionScheduledAt.Value).TotalDays;
        return effectiveWindow <= 0 || effectiveWindow > maximumDeletionWindowDays + 1;
    }

    private static bool IsPermissionError(string providerErrorCode) =>
        providerErrorCode.Contains("access", StringComparison.OrdinalIgnoreCase) ||
        providerErrorCode.Contains("permission", StringComparison.OrdinalIgnoreCase) ||
        providerErrorCode.Contains("denied", StringComparison.OrdinalIgnoreCase) ||
        providerErrorCode.Contains("unauthorized", StringComparison.OrdinalIgnoreCase);

    private static bool HasDestroyedMarker(ElectionAdminOnlyProtectedTallyEnvelopeRecord envelope) =>
        envelope.DestroyedAt.HasValue &&
        string.Equals(
            envelope.SealedTallyPrivateScalar,
            AdminOnlyProtectedTallyEnvelopeCryptoConstants.DestroyedEnvelopeMarker,
            StringComparison.Ordinal);

    private static string ResolveNoRepairResult(AdminOnlyProtectedTallyCustodyReconciliationRunMode runMode) =>
        runMode == AdminOnlyProtectedTallyCustodyReconciliationRunMode.DryRun
            ? "dry_run_no_change"
            : "manual_operator_action_required";

    private static string BuildPrivateCustodyRowReference(ElectionId electionId, string selectedProfileId) =>
        $"elections/admin-only-protected-tally/{electionId}/{selectedProfileId}";
}
