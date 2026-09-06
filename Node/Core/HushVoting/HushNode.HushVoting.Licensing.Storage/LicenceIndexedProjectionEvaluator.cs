namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Pure, deterministic FEAT-015 projection evaluation: given a subject row and an evaluation
/// instant, decide Active/NoActive and produce the client-safe entitlement snapshot. Contains no
/// I/O and no mutation — upper-exclusive expiry is observational and no state is ever written.
/// </summary>
internal static class LicenceIndexedProjectionEvaluator
{
    public static IndexedEntitlementReadResult Evaluate(
        LicenceSubjectEntity subjectRow,
        DateTime evaluationUtc)
    {
        ArgumentNullException.ThrowIfNull(subjectRow);

        // Partial-unique single active assignment per subject (FEAT-013 invariant). Retained
        // history is never selected: only the top effective-interval row can be current.
        var candidate = subjectRow.Assignments
            .Where(a => a.LifecycleStatus == LicencePersistenceVocabulary.LifecycleActive)
            .OrderByDescending(a => a.EffectiveFromUtc)
            .FirstOrDefault();

        if (candidate is null
            || candidate.EffectiveFromUtc > evaluationUtc
            || (candidate.ExpiresAtUtc is not null && candidate.ExpiresAtUtc <= evaluationUtc))
        {
            return IndexedEntitlementReadResult.NoActive();
        }

        return IndexedEntitlementReadResult.Active(Project(subjectRow, candidate));
    }

    private static EffectiveLicenceEntitlement Project(
        LicenceSubjectEntity subjectRow,
        LicenceAssignmentEntity assignment)
    {
        return new EffectiveLicenceEntitlement(
            subjectRow.LicenceSubjectId,
            assignment.LicenceAssignmentId,
            assignment.PlanId,
            assignment.PlanFamily,
            assignment.UpgradeRank,
            assignment.EligibleVoterCap,
            assignment.UnlimitedElectionPolicy,
            assignment.TermKind,
            assignment.TermYears,
            assignment.AllowedGovernanceOptionIds,
            assignment.Source,
            assignment.EffectiveFromUtc,
            assignment.ExpiresAtUtc,
            assignment.AssignedCatalogueVersion,
            assignment.AssignedCatalogueDigestSha256,
            subjectRow.EntitlementRevision);
    }
}
