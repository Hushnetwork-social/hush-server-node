namespace HushShared.HushVoting.Licensing.Model;

/// <summary>Outcome of a catalogue build attempt: complete validation plus optional immutable snapshot.</summary>
public sealed record HushVotingLicenceCatalogueBuildResult(
    HushVotingLicenceCatalogueValidationResult Validation,
    HushVotingLicenceCatalogue? Catalogue)
{
    public bool IsValid => Validation.IsValid && Catalogue is not null;

    public static HushVotingLicenceCatalogueBuildResult Rejected(
        IEnumerable<HushVotingLicenceValidationFailure> failures) =>
        new(HushVotingLicenceCatalogueValidationResult.FromFailures(failures), Catalogue: null);

    public static HushVotingLicenceCatalogueBuildResult Accepted(HushVotingLicenceCatalogue catalogue) =>
        new(HushVotingLicenceCatalogueValidationResult.Valid, catalogue);
}

/// <summary>
/// Pure complete semantic validator for an exact v1 catalogue candidate. It compares every plan and
/// mapping against the canonical v1 definitions and accumulates ALL deterministic failures in stable
/// order (code, then field path). It never throws for expected invalid input and never returns a
/// partial catalogue. Structural bounds (known ids/versions, no duplicates) are enforced earlier by
/// the aggregate constructors; the strict loader in Phase 6 handles schema/digest/path concerns.
///
/// Stable-code mapping used here (documented contract for downstream features):
///   plan set / missing / extra / placeholder / unknown        -> LIC_CAT_PLAN_SET_INVALID
///   Direct Free default role / availability policy drift      -> LIC_CAT_DEFAULT_INVALID
///   rank and display-order drift / duplicate rank             -> LIC_CAT_RANK_INVALID
///   term drift                                                -> LIC_CAT_TERM_INVALID
///   cap / unlimited-elections / Enterprise absence drift      -> LIC_CAT_LIMIT_INVALID
///   governance option set drift / non-cumulative / forbidden  -> LIC_CAT_GOVERNANCE_INVALID
///   display copy drift or forbidden copy tokens               -> LIC_CAT_COPY_UNSAFE
/// </summary>
public static class HushVotingLicenceCatalogueValidator
{
    /// <summary>Substrings that are never allowed in display copy (release data must stay safe).</summary>
    private static readonly string[] ForbiddenCopyTokens =
    [
        "price", "pricing", "currency", "payment", "billing", "invoice", "provider key",
        "providerKey", "internal note", "legal claim", "readiness claim", "not legal advice",
    ];

    public static HushVotingLicenceCatalogueBuildResult ValidateAndBuild(
        IReadOnlyList<HushVotingLicencePlan> candidatePlans,
        IReadOnlyList<HushVotingProfileCompatibilityEntry> candidateMappings)
    {
        ArgumentNullException.ThrowIfNull(candidatePlans);
        ArgumentNullException.ThrowIfNull(candidateMappings);

        var failures = new List<HushVotingLicenceValidationFailure>();
        var canonical = HushVotingLicenceCatalogueV1.CreateCatalogue();
        var canonicalById = canonical.Plans.ToDictionary(static p => p.Id.Value, StringComparer.Ordinal);

        // 1) Plan set: exact five known plans, no missing/extra/duplicate/placeholder.
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var i = 0; i < candidatePlans.Count; i++)
        {
            var plan = candidatePlans[i];
            var path = $"/plans/{i}/planId";

            if (!seen.Add(plan.Id.Value))
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatPlanSetInvalid,
                    path,
                    $"Duplicate plan id '{plan.Id.Value}'."));
            }

            if (!plan.Id.IsKnown)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatPlanSetInvalid,
                    path,
                    $"Plan id '{plan.Id.Value}' is not one of the accepted v1 plans."));
                continue;
            }

            if (!canonicalById.ContainsKey(plan.Id.Value))
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatPlanSetInvalid,
                    path,
                    $"Plan id '{plan.Id.Value}' is unknown or a forbidden placeholder in v1."));
            }
        }

        var required = HushVotingLicencePlanId.Known.Select(static id => id.Value).ToHashSet(StringComparer.Ordinal);
        var present = candidatePlans.Select(static p => p.Id.Value).ToHashSet(StringComparer.Ordinal);
        foreach (var missing in required.Where(id => !present.Contains(id)).OrderBy(static id => id, StringComparer.Ordinal))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatPlanSetInvalid,
                "/plans",
                $"Required v1 plan '{missing}' is missing."));
        }

        // 2) Per-plan exact semantic comparison against canonical v1.
        for (var i = 0; i < candidatePlans.Count; i++)
        {
            var candidate = candidatePlans[i];
            if (!candidate.Id.IsKnown || !canonicalById.ContainsKey(candidate.Id.Value))
            {
                continue;
            }

            var canonicalPlan = canonicalById[candidate.Id.Value];
            ValidatePlanSemantics(candidate, canonicalPlan, $"/plans/{i}", failures);
        }

        // 3) Exactly one enabled Default and it must be Direct Free.
        var defaults = candidatePlans
            .Where(static p => p.Availability == HushVotingLicenceAvailability.Default)
            .Select(static p => p.Id.Value)
            .OrderBy(static v => v, StringComparer.Ordinal)
            .ToArray();

        if (defaults.Length != 1 || defaults[0] != HushVotingLicencePlanId.DirectFreeValue)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatDefaultInvalid,
                "/plans/availability",
                $"Direct Free must be the single enabled default (found: {string.Join(",", defaults)})."));
        }

        // 4) Governance option sets must be exact and cumulative (validated per plan above),
        //    and every option must be represented in the mapping for both binding modes.
        ValidateMappings(candidatePlans, candidateMappings, failures);

        if (failures.Count == 0)
        {
            try
            {
                var catalogue = new HushVotingLicenceCatalogue(
                    HushVotingLicenceCatalogueVersion.V1,
                    candidatePlans,
                    candidateMappings);
                return HushVotingLicenceCatalogueBuildResult.Accepted(catalogue);
            }
            catch (Exception ex)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatPlanSetInvalid,
                    "/plans",
                    $"Snapshot construction failed: {ex.Message}"));
            }
        }

        return HushVotingLicenceCatalogueBuildResult.Rejected(failures);
    }

    private static void ValidatePlanSemantics(
        HushVotingLicencePlan candidate,
        HushVotingLicencePlan canonical,
        string basePath,
        List<HushVotingLicenceValidationFailure> failures)
    {
        if (candidate.Family != canonical.Family)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatPlanSetInvalid,
                $"{basePath}/family",
                $"Plan '{candidate.Id.Value}' family must be '{canonical.Family}'."));
        }

        if (candidate.Availability != canonical.Availability)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatDefaultInvalid,
                $"{basePath}/availability",
                $"Plan '{candidate.Id.Value}' availability must be '{canonical.Availability}'."));
        }

        if (candidate.UpgradeRank != canonical.UpgradeRank)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatRankInvalid,
                $"{basePath}/upgradeRank",
                $"Plan '{candidate.Id.Value}' rank must be {canonical.UpgradeRank}."));
        }

        if (candidate.DisplayOrder != canonical.DisplayOrder)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatRankInvalid,
                $"{basePath}/displayOrder",
                $"Plan '{candidate.Id.Value}' display order must be {canonical.DisplayOrder}."));
        }

        if (candidate.Term != canonical.Term)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatTermInvalid,
                $"{basePath}/term",
                $"Plan '{candidate.Id.Value}' term must be '{canonical.Term.SafeDescription}'."));
        }

        if (candidate.EligibleVoterCap != canonical.EligibleVoterCap ||
            candidate.UnlimitedElections != canonical.UnlimitedElections)
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatLimitInvalid,
                $"{basePath}/limit",
                $"Plan '{candidate.Id.Value}' cap/unlimited policy must match v1 "
                + $"({DescribeCap(canonical.EligibleVoterCap)}, unlimited={canonical.UnlimitedElections})."));
        }

        if (!GovernanceSetsMatch(candidate.GovernanceOptions, canonical.GovernanceOptions))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatGovernanceInvalid,
                $"{basePath}/governanceOptions",
                $"Plan '{candidate.Id.Value}' governance options must be exactly "
                + $"[{string.Join(", ", canonical.GovernanceOptions.Select(static o => o.Id.Value))}]."));
        }

        if (!string.Equals(candidate.DisplayName, canonical.DisplayName, StringComparison.Ordinal) ||
            !string.Equals(candidate.SafeDescription, canonical.SafeDescription, StringComparison.Ordinal) ||
            !string.Equals(candidate.UnavailableSafeReason, canonical.UnavailableSafeReason, StringComparison.Ordinal))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatCopyUnsafe,
                $"{basePath}/copy",
                $"Plan '{candidate.Id.Value}' display copy must equal the accepted v1 release copy."));
        }

        foreach (var token in ForbiddenCopyTokens)
        {
            if (ContainsOrdinalIgnoreCase(candidate.DisplayName, token) ||
                ContainsOrdinalIgnoreCase(candidate.SafeDescription, token) ||
                (candidate.UnavailableSafeReason is not null &&
                 ContainsOrdinalIgnoreCase(candidate.UnavailableSafeReason, token)))
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatCopyUnsafe,
                    $"{basePath}/copy",
                    $"Plan '{candidate.Id.Value}' copy contains forbidden token '{token}'."));
            }
        }
    }

    private static void ValidateMappings(
        IReadOnlyList<HushVotingLicencePlan> plans,
        IReadOnlyList<HushVotingProfileCompatibilityEntry> mappings,
        List<HushVotingLicenceValidationFailure> failures)
    {
        var mappingKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in mappings)
        {
            if (!entry.GovernanceOptionId.IsKnown)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatProfileMismatch,
                    "/profileCompatibility/governanceOptionId",
                    $"Mapping references unknown governance option '{entry.GovernanceOptionId.Value}'."));
                continue;
            }

            var key = $"{entry.GovernanceOptionId.Value}|{entry.BindingStatus}";
            if (!mappingKeys.Add(key))
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatProfileMismatch,
                    "/profileCompatibility",
                    $"Duplicate mapping for option '{entry.GovernanceOptionId.Value}' binding '{entry.BindingStatus}'."));
            }

            var expected = HushVotingProfileCompatibilityV1.Resolve(
                entry.GovernanceOptionId,
                entry.BindingStatus);
            if (expected is null ||
                !string.Equals(entry.RuntimeProfileId, expected.RuntimeProfileId, StringComparison.Ordinal) ||
                entry.DevOnly != expected.DevOnly)
            {
                failures.Add(new HushVotingLicenceValidationFailure(
                    HushVotingLicenceValidationCodes.LicCatProfileMismatch,
                    "/profileCompatibility",
                    $"Mapping for option '{entry.GovernanceOptionId.Value}' binding '{entry.BindingStatus}' "
                    + $"must resolve to '{expected?.RuntimeProfileId ?? "<none>"}'."));
            }
        }

        // Every plan option that supports both modes must have both mapping keys present.
        var requiredKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var plan in plans)
        {
            if (plan.Id.Value == HushVotingLicencePlanId.EnterpriseValue)
            {
                continue; // Enterprise has no executable mapping in v1.
            }

            foreach (var option in plan.GovernanceOptions)
            {
                requiredKeys.Add($"{option.Id.Value}|{HushVotingBindingStatus.NonBinding}");
                requiredKeys.Add($"{option.Id.Value}|{HushVotingBindingStatus.Binding}");
            }
        }

        foreach (var missing in requiredKeys.Except(mappingKeys).OrderBy(static k => k, StringComparer.Ordinal))
        {
            failures.Add(new HushVotingLicenceValidationFailure(
                HushVotingLicenceValidationCodes.LicCatProfileMissing,
                "/profileCompatibility",
                $"Required mapping '{missing}' is missing."));
        }
    }

    private static bool GovernanceSetsMatch(
        IReadOnlyList<HushVotingGovernanceOption> actual,
        IReadOnlyList<HushVotingGovernanceOption> expected)
    {
        var actualIds = actual.Select(static o => o.Id.Value).OrderBy(static v => v, StringComparer.Ordinal).ToArray();
        var expectedIds = expected.Select(static o => o.Id.Value).OrderBy(static v => v, StringComparer.Ordinal).ToArray();
        return actualIds.SequenceEqual(expectedIds);
    }

    private static string DescribeCap(int? cap) => cap is null ? "customer-specific/not configured" : cap.Value.ToString();

    private static bool ContainsOrdinalIgnoreCase(string text, string token) =>
        text.Contains(token, StringComparison.OrdinalIgnoreCase);
}
