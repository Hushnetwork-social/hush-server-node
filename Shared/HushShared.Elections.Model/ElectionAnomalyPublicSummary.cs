namespace HushShared.Elections.Model;

public static class ElectionAnomalyPublicSummarySchemaIds
{
    public const string PublicAnomalySummaryV1 = "public-anomaly-summary-v1";
    public const string Current = PublicAnomalySummaryV1;
}

public static class ElectionAnomalyPublicSummarySuppressionPolicyIds
{
    public const string AnomalyPublicSummaryV1 = "anomaly-public-summary-v1";
    public const string Current = AnomalyPublicSummaryV1;
}

public static class ElectionAnomalyPublicSummaryCountModeIds
{
    public const string Exact = "exact";
    public const string Aggregated = "aggregated";
    public const string Suppressed = "suppressed";
}

public static class ElectionAnomalyPublicSummarySuppressionReasonIds
{
    public const string LowCountCategory = "low_count_category";
    public const string IdentifyingRoleCategory = "identifying_role_category";
    public const string SmallElectionIdentifiability = "small_election_identifiability";
    public const string RestrictedEvidenceOnly = "restricted_evidence_only";
    public const string AggregationNotSafe = "aggregation_not_safe";
}

public static class ElectionAnomalyPublicArtifactScanStatusIds
{
    public const string Passed = "passed";
    public const string Failed = "failed";
}

public sealed record PublicAnomalySummary(
    string SchemaId,
    string SuppressionPolicyId,
    string ElectionId,
    string? SourceManifestHash,
    int? TotalThreadCount,
    string TotalThreadCountMode,
    IReadOnlyList<PublicAnomalySummaryBucket> VisibleBuckets,
    int AggregatedBucketCount,
    int SuppressedThreadCount,
    IReadOnlyList<string> SuppressionReasonIds,
    Guid? RestrictedManifestArtifactId,
    string? RestrictedManifestHash,
    DateTime GeneratedAt);

public sealed record PublicAnomalySummaryBucket(
    string CategoryId,
    string CountMode,
    int? PublicCount,
    IReadOnlyList<string> SuppressionReasonIds,
    IReadOnlyList<string> SourceCategoryIds);

public sealed record ElectionAnomalyPublicSummaryBuildRequest(
    string ElectionId,
    AnomalyIntakeManifest? RestrictedAnomalyIntakeManifest,
    Guid? RestrictedManifestArtifactId,
    DateTime GeneratedAtUtc,
    IReadOnlyList<ElectionAnomalyPublicSummaryPrivateThreadContext>? PrivateThreadContexts = null);

public sealed record ElectionAnomalyPublicSummaryPrivateThreadContext(
    Guid AnomalyThreadId,
    string? SubmitterRoleContextId = null,
    bool IsSmallElectionIdentifiabilityRisk = false,
    bool ForceRestrictedEvidenceOnly = false);

public sealed record PublicArtifactForbiddenFieldScanResult(
    string StatusId,
    IReadOnlyList<string> MatchedFieldNames,
    IReadOnlyList<string> MatchedValues)
{
    public bool Passed => string.Equals(StatusId, ElectionAnomalyPublicArtifactScanStatusIds.Passed, StringComparison.Ordinal);
}

public static class ElectionAnomalyPublicSummaryBuilder
{
    public const int MinimumExactPublicCount = 3;

    public static PublicAnomalySummary Build(ElectionAnomalyPublicSummaryBuildRequest request)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ElectionId);

        var manifest = request.RestrictedAnomalyIntakeManifest;
        var manifestHash = manifest is null
            ? null
            : ElectionAnomalyIntakeManifestHasher.ComputeHash(manifest);
        var threads = manifest?.Threads ?? Array.Empty<AnomalyIntakeManifestThread>();
        var privateContextByThreadId = (request.PrivateThreadContexts ?? Array.Empty<ElectionAnomalyPublicSummaryPrivateThreadContext>())
            .GroupBy(x => x.AnomalyThreadId)
            .ToDictionary(x => x.Key, x => x.First());
        var topLevelReasons = new SortedSet<string>(StringComparer.Ordinal);
        var buckets = new List<PublicAnomalySummaryBucket>();
        var unsafeGroups = new List<UnsafeCategoryGroup>();

        foreach (var group in threads
            .GroupBy(x => x.CategoryId, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var groupedThreads = group
                .OrderBy(x => x.AnomalyThreadId)
                .ToArray();
            var reasons = DetermineSuppressionReasons(group.Key, groupedThreads, privateContextByThreadId);

            if (reasons.Count == 0)
            {
                buckets.Add(new PublicAnomalySummaryBucket(
                    group.Key,
                    ElectionAnomalyPublicSummaryCountModeIds.Exact,
                    groupedThreads.Length,
                    Array.Empty<string>(),
                    [group.Key]));
                continue;
            }

            foreach (var reason in reasons)
            {
                topLevelReasons.Add(reason);
            }

            unsafeGroups.Add(new UnsafeCategoryGroup(group.Key, groupedThreads, reasons.ToArray()));
        }

        var suppressedThreadCount = 0;
        var aggregatedBucketCount = 0;
        if (unsafeGroups.Count > 0)
        {
            if (CanAggregate(unsafeGroups, privateContextByThreadId))
            {
                var aggregateReasons = unsafeGroups
                    .SelectMany(x => x.ReasonIds)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();
                var sourceCategoryIds = unsafeGroups
                    .Select(x => x.CategoryId)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(x => x, StringComparer.Ordinal)
                    .ToArray();

                buckets.Add(new PublicAnomalySummaryBucket(
                    ElectionAnomalyCategoryIds.OtherProcessAnomaly,
                    ElectionAnomalyPublicSummaryCountModeIds.Aggregated,
                    unsafeGroups.Sum(x => x.Threads.Count),
                    aggregateReasons,
                    sourceCategoryIds));
                aggregatedBucketCount = sourceCategoryIds.Length;
            }
            else
            {
                topLevelReasons.Add(ElectionAnomalyPublicSummarySuppressionReasonIds.AggregationNotSafe);
                foreach (var unsafeGroup in unsafeGroups)
                {
                    var reasons = unsafeGroup.ReasonIds
                        .Append(ElectionAnomalyPublicSummarySuppressionReasonIds.AggregationNotSafe)
                        .Distinct(StringComparer.Ordinal)
                        .OrderBy(x => x, StringComparer.Ordinal)
                        .ToArray();
                    buckets.Add(new PublicAnomalySummaryBucket(
                        unsafeGroup.CategoryId,
                        ElectionAnomalyPublicSummaryCountModeIds.Suppressed,
                        PublicCount: null,
                        reasons,
                        [unsafeGroup.CategoryId]));
                    suppressedThreadCount += unsafeGroup.Threads.Count;
                }
            }
        }

        var totalThreadCountMode = ResolveTotalThreadCountMode(threads.Count, topLevelReasons);
        return new PublicAnomalySummary(
            ElectionAnomalyPublicSummarySchemaIds.Current,
            ElectionAnomalyPublicSummarySuppressionPolicyIds.Current,
            request.ElectionId,
            manifestHash,
            totalThreadCountMode == ElectionAnomalyPublicSummaryCountModeIds.Exact ? threads.Count : null,
            totalThreadCountMode,
            buckets
                .OrderBy(x => x.CountMode == ElectionAnomalyPublicSummaryCountModeIds.Exact ? 0 : x.CountMode == ElectionAnomalyPublicSummaryCountModeIds.Aggregated ? 1 : 2)
                .ThenBy(x => x.CategoryId, StringComparer.Ordinal)
                .ToArray(),
            aggregatedBucketCount,
            suppressedThreadCount,
            topLevelReasons
                .OrderBy(x => x, StringComparer.Ordinal)
                .ToArray(),
            request.RestrictedManifestArtifactId,
            manifestHash,
            NormalizeUtc(request.GeneratedAtUtc));
    }

    private static SortedSet<string> DetermineSuppressionReasons(
        string categoryId,
        IReadOnlyList<AnomalyIntakeManifestThread> threads,
        IReadOnlyDictionary<Guid, ElectionAnomalyPublicSummaryPrivateThreadContext> privateContextByThreadId)
    {
        var reasons = new SortedSet<string>(StringComparer.Ordinal);
        if (threads.Count < MinimumExactPublicCount)
        {
            reasons.Add(ElectionAnomalyPublicSummarySuppressionReasonIds.LowCountCategory);
        }

        var contexts = threads
            .Select(thread => privateContextByThreadId.GetValueOrDefault(thread.AnomalyThreadId))
            .Where(context => context is not null)
            .Cast<ElectionAnomalyPublicSummaryPrivateThreadContext>()
            .ToArray();

        if (contexts.Any(x => x.ForceRestrictedEvidenceOnly))
        {
            reasons.Add(ElectionAnomalyPublicSummarySuppressionReasonIds.RestrictedEvidenceOnly);
        }

        if (contexts.Any(x => x.IsSmallElectionIdentifiabilityRisk))
        {
            reasons.Add(ElectionAnomalyPublicSummarySuppressionReasonIds.SmallElectionIdentifiability);
        }

        var identifyingRoleContext = contexts
            .Where(x => !string.IsNullOrWhiteSpace(x.SubmitterRoleContextId))
            .GroupBy(x => x.SubmitterRoleContextId!, StringComparer.Ordinal)
            .Any(x => x.Count() < MinimumExactPublicCount);
        if (identifyingRoleContext)
        {
            reasons.Add(ElectionAnomalyPublicSummarySuppressionReasonIds.IdentifyingRoleCategory);
        }

        return reasons;
    }

    private static bool CanAggregate(
        IReadOnlyList<UnsafeCategoryGroup> unsafeGroups,
        IReadOnlyDictionary<Guid, ElectionAnomalyPublicSummaryPrivateThreadContext> privateContextByThreadId)
    {
        var totalUnsafeCount = unsafeGroups.Sum(x => x.Threads.Count);
        if (totalUnsafeCount < MinimumExactPublicCount)
        {
            return false;
        }

        if (unsafeGroups.SelectMany(x => x.ReasonIds).Any(reason =>
                string.Equals(reason, ElectionAnomalyPublicSummarySuppressionReasonIds.SmallElectionIdentifiability, StringComparison.Ordinal) ||
                string.Equals(reason, ElectionAnomalyPublicSummarySuppressionReasonIds.RestrictedEvidenceOnly, StringComparison.Ordinal)))
        {
            return false;
        }

        var contexts = unsafeGroups
            .SelectMany(x => x.Threads)
            .Select(thread => privateContextByThreadId.GetValueOrDefault(thread.AnomalyThreadId))
            .Where(context => context is not null)
            .Cast<ElectionAnomalyPublicSummaryPrivateThreadContext>()
            .Where(context => !string.IsNullOrWhiteSpace(context.SubmitterRoleContextId))
            .ToArray();

        return contexts
            .GroupBy(x => x.SubmitterRoleContextId!, StringComparer.Ordinal)
            .All(x => x.Count() >= MinimumExactPublicCount);
    }

    private static string ResolveTotalThreadCountMode(int threadCount, ISet<string> topLevelReasons)
    {
        if (threadCount == 0 || threadCount >= MinimumExactPublicCount)
        {
            return ElectionAnomalyPublicSummaryCountModeIds.Exact;
        }

        topLevelReasons.Add(ElectionAnomalyPublicSummarySuppressionReasonIds.SmallElectionIdentifiability);
        return ElectionAnomalyPublicSummaryCountModeIds.Suppressed;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(value, DateTimeKind.Utc)
            : value.ToUniversalTime();

    private sealed record UnsafeCategoryGroup(
        string CategoryId,
        IReadOnlyList<AnomalyIntakeManifestThread> Threads,
        IReadOnlyList<string> ReasonIds);
}

public static class ElectionAnomalyPublicArtifactPrivacyScanner
{
    public static IReadOnlyList<string> DefaultForbiddenFieldNames { get; } =
    [
        "submitterActorName",
        "submitterActorPublicAddress",
        "submitterPersonScopeId",
        "rawExternalClaimantReference",
        "claimantReferenceHash",
        "encryptedBody",
        "authorityResponseBody",
        "encryptedPayloadReference",
        "recipientPublicAddress",
        "encryptedContentKey",
        "contentKeyWrap",
        "ownerIdentityContext",
    ];

    public static PublicArtifactForbiddenFieldScanResult Scan(
        string content,
        IEnumerable<string>? representativeSensitiveValues = null,
        IEnumerable<string>? forbiddenFieldNames = null)
    {
        ArgumentNullException.ThrowIfNull(content);

        var fields = (forbiddenFieldNames ?? DefaultForbiddenFieldNames)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var values = (representativeSensitiveValues ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var matchedFields = fields
            .Where(field => content.Contains(field, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var matchedValues = values
            .Where(value => content.Contains(value, StringComparison.Ordinal))
            .ToArray();
        var statusId = matchedFields.Length == 0 && matchedValues.Length == 0
            ? ElectionAnomalyPublicArtifactScanStatusIds.Passed
            : ElectionAnomalyPublicArtifactScanStatusIds.Failed;

        return new PublicArtifactForbiddenFieldScanResult(
            statusId,
            matchedFields,
            matchedValues);
    }
}

public static class ElectionAnomalyRestrictedArtifactPrivacyScanner
{
    public static IReadOnlyList<string> AuditorUnsafeRestrictedManifestFieldNames { get; } =
    [
        "submitterActorName",
        "submitterActorPublicAddress",
        "submitterPublicAddress",
        "submitterPersonScopeId",
        "rawExternalClaimantReference",
        "recipientPublicAddress",
        "encryptedContentKey",
        "contentKeyWrap",
        "ownerIdentityContext",
    ];

    public static PublicArtifactForbiddenFieldScanResult ScanAuditorSafeManifest(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var matchedFields = AuditorUnsafeRestrictedManifestFieldNames
            .Where(field => content.Contains(field, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();
        var statusId = matchedFields.Length == 0
            ? ElectionAnomalyPublicArtifactScanStatusIds.Passed
            : ElectionAnomalyPublicArtifactScanStatusIds.Failed;

        return new PublicArtifactForbiddenFieldScanResult(
            statusId,
            matchedFields,
            Array.Empty<string>());
    }
}
