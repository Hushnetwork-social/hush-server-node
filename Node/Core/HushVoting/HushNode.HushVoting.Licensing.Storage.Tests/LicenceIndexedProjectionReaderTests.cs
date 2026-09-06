using FluentAssertions;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// FEAT-015 deterministic projection-evaluation unit tests: interval membership, upper-exclusive
/// expiry (observational, no writes), retained-history top selection, and snapshot mapping.
/// </summary>
public sealed class LicenceIndexedProjectionReaderTests
{
    private static readonly Guid SubjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid DirectFreeAssignmentId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid VeritasAssignmentId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly DateTime FixedNow = new(2026, 9, 6, 12, 0, 0, DateTimeKind.Utc);

    private static LicenceSubjectEntity Subject(params LicenceAssignmentEntity[] assignments)
    {
        var subject = new LicenceSubjectEntity
        {
            LicenceSubjectId = SubjectId,
            SubjectType = LicencePersistenceVocabulary.SubjectTypeIdentity,
            CanonicalPublicSigningAddress = "0xalice",
            IdentityCreationBlockIndex = 5,
            CreatedAtUtc = FixedNow.AddDays(-30),
            EntitlementRevision = 9,
        };
        foreach (var assignment in assignments)
        {
            assignment.LicenceAssignmentId = assignment.LicenceAssignmentId == Guid.Empty
                ? DirectFreeAssignmentId
                : assignment.LicenceAssignmentId;
            assignment.LicenceSubjectId = SubjectId;
            subject.Assignments.Add(assignment);
        }

        return subject;
    }

    private static LicenceAssignmentEntity Assignment(
        Guid id,
        string planId,
        string family,
        int rank,
        DateTime effectiveFromUtc,
        DateTime? expiresAtUtc = null,
        string lifecycle = LicencePersistenceVocabulary.LifecycleActive) =>
        new()
        {
            LicenceAssignmentId = id,
            PlanId = planId,
            PlanFamily = family,
            UpgradeRank = rank,
            EligibleVoterCap = rank == 0 ? 100 : null,
            UnlimitedElectionPolicy = true,
            TermKind = expiresAtUtc is null ? LicencePersistenceVocabulary.TermPerpetual : LicencePersistenceVocabulary.TermAnnual,
            TermYears = expiresAtUtc is null ? 0 : 1,
            AllowedGovernanceOptionIds = Array.Empty<string>(),
            Source = "baseline_free",
            EffectiveFromUtc = effectiveFromUtc,
            ExpiresAtUtc = expiresAtUtc,
            LifecycleStatus = lifecycle,
            AssignedCatalogueVersion = "1.0.0",
            AssignedCatalogueDigestSha256 = "abc",
        };

    [Fact]
    public void Active_veritas_within_interval_projects_deterministic_snapshot()
    {
        var assignment = Assignment(VeritasAssignmentId, "hushvoting.veritas.500", "Veritas", 500, FixedNow.AddDays(-10), FixedNow.AddYears(1).AddDays(-10));
        var subject = Subject(assignment);

        var result = LicenceIndexedProjectionEvaluator.Evaluate(subject, FixedNow);

        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(IndexedEntitlementReadOutcome.Active);
        result.Entitlement.Should().NotBeNull();
        result.Entitlement!.PlanId.Should().Be("hushvoting.veritas.500");
        result.Entitlement.PlanFamily.Should().Be("Veritas");
        result.Entitlement.UpgradeRank.Should().Be(500);
        result.Entitlement.LicenceSubjectId.Should().Be(SubjectId);
        result.Entitlement.LicenceAssignmentId.Should().Be(VeritasAssignmentId);
        result.Entitlement.EffectiveFromUtc.Should().Be(FixedNow.AddDays(-10));
        result.Entitlement.ExpiresAtUtc.Should().Be(FixedNow.AddYears(1).AddDays(-10));
        result.Entitlement.EntitlementRevision.Should().Be(9);
        result.Entitlement.Source.Should().Be("baseline_free");
    }

    [Fact]
    public void Upper_exclusive_expiry_is_observational_no_active_at_expiry_instant()
    {
        var expiresAt = FixedNow.AddDays(5);
        var assignment = Assignment(VeritasAssignmentId, "hushvoting.veritas.500", "Veritas", 500, FixedNow.AddDays(-30), expiresAt);
        var subject = Subject(assignment);

        var before = LicenceIndexedProjectionEvaluator.Evaluate(subject, expiresAt.AddMilliseconds(-1));
        var atExpiry = LicenceIndexedProjectionEvaluator.Evaluate(subject, expiresAt);
        var afterExpiry = LicenceIndexedProjectionEvaluator.Evaluate(subject, expiresAt.AddMilliseconds(1));

        before.Outcome.Should().Be(IndexedEntitlementReadOutcome.Active);
        atExpiry.Outcome.Should().Be(IndexedEntitlementReadOutcome.NoActive, "expiry is upper-exclusive at the same UTC calendar instant");
        afterExpiry.Outcome.Should().Be(IndexedEntitlementReadOutcome.NoActive);
    }

    [Fact]
    public void No_effective_assignment_returns_no_active_not_unavailable()
    {
        var subject = Subject(); // never had a licence
        var result = LicenceIndexedProjectionEvaluator.Evaluate(subject, FixedNow);

        result.IsSuccess.Should().BeTrue();
        result.Outcome.Should().Be(IndexedEntitlementReadOutcome.NoActive);
        result.Entitlement.Should().BeNull();
    }

    [Fact]
    public void Retained_history_is_never_selected_as_current()
    {
        var expiredDirectFree = Assignment(DirectFreeAssignmentId, "hushvoting.direct.free", "Direct Free", 0, FixedNow.AddDays(-300), null);
        expiredDirectFree.LifecycleStatus = LicencePersistenceVocabulary.LifecycleSuperseded;
        var veritas = Assignment(VeritasAssignmentId, "hushvoting.veritas.2000", "Veritas", 2000, FixedNow.AddDays(-10), FixedNow.AddYears(1).AddDays(-10));
        var subject = Subject(expiredDirectFree, veritas);

        var result = LicenceIndexedProjectionEvaluator.Evaluate(subject, FixedNow);

        result.Outcome.Should().Be(IndexedEntitlementReadOutcome.Active);
        result.Entitlement!.PlanId.Should().Be("hushvoting.veritas.2000");
        result.Entitlement.LicenceAssignmentId.Should().Be(VeritasAssignmentId);
    }

    [Fact]
    public void Future_effective_assignment_is_not_current_yet()
    {
        var future = Assignment(VeritasAssignmentId, "hushvoting.veritas.500", "Veritas", 500, FixedNow.AddDays(1), FixedNow.AddYears(1).AddDays(1));
        var subject = Subject(future);

        var result = LicenceIndexedProjectionEvaluator.Evaluate(subject, FixedNow);

        result.Outcome.Should().Be(IndexedEntitlementReadOutcome.NoActive);
    }
}
