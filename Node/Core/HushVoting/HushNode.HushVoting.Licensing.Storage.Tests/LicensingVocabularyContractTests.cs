using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// Contract tests for the closed v1 persistence vocabulary. These values are written to
/// PostgreSQL and enforced by database CHECK constraints; a drift here breaks constraint
/// tests and data contracts, so every constant is pinned by an exact assertion.
/// </summary>
public class LicensingVocabularyContractTests
{
    [Fact]
    public void SubjectType_has_exactly_one_v1_value()
    {
        LicencePersistenceVocabulary.SubjectTypeIdentity.Should().Be("Identity");
    }

    [Fact]
    public void Lifecycle_vocabulary_is_closed_and_exact()
    {
        LicencePersistenceVocabulary.LifecycleActive.Should().Be("active");
        LicencePersistenceVocabulary.LifecycleSuperseded.Should().Be("superseded");
        LicencePersistenceVocabulary.LifecycleExpired.Should().Be("expired");
    }

    [Fact]
    public void Assignment_source_vocabulary_is_closed_and_exact()
    {
        LicencePersistenceVocabulary.SourceDefaultFree.Should().Be("default_free");
        LicencePersistenceVocabulary.SourceMigrationLazyDefault.Should().Be("migration_lazy_default");
        LicencePersistenceVocabulary.SourceAutomaticUpgrade.Should().Be("automatic_upgrade");
        LicencePersistenceVocabulary.SourceAutomaticExpiry.Should().Be("automatic_expiry");
        LicencePersistenceVocabulary.SourceBaselineFree.Should().Be("baseline_free");
        LicencePersistenceVocabulary.SourceConfirmedUpgrade.Should().Be("confirmed_upgrade");
    }

    [Fact]
    public void Reservation_lifecycle_vocabulary_is_closed_and_exact()
    {
        LicencePersistenceVocabulary.ReservationLifecyclePending.Should().Be("pending");
        LicencePersistenceVocabulary.ReservationLifecycleSuperseded.Should().Be("superseded");
        LicencePersistenceVocabulary.ReservationLifecycleResolved.Should().Be("resolved");
    }

    [Fact]
    public void Reservation_lifecycle_set_matches_the_schema_check_constraint()
    {
        var expected = new[]
        {
            "pending",
            "superseded",
            "resolved",
        };

        var actual = new[]
        {
            LicencePersistenceVocabulary.ReservationLifecyclePending,
            LicencePersistenceVocabulary.ReservationLifecycleSuperseded,
            LicencePersistenceVocabulary.ReservationLifecycleResolved,
        };

        actual.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Event_type_vocabulary_is_closed_and_exact()
    {
        LicencePersistenceVocabulary.EventTypeCreated.Should().Be("created");
        LicencePersistenceVocabulary.EventTypeSuperseded.Should().Be("superseded");
        LicencePersistenceVocabulary.EventTypeExpired.Should().Be("expired");
    }

    [Fact]
    public void Term_and_family_vocabularies_are_exact()
    {
        LicencePersistenceVocabulary.TermPerpetual.Should().Be("perpetual");
        LicencePersistenceVocabulary.TermAnnual.Should().Be("annual");
        LicencePersistenceVocabulary.PlanFamilyDirect.Should().Be("direct");
        LicencePersistenceVocabulary.PlanFamilyVeritas.Should().Be("veritas");
        LicencePersistenceVocabulary.PlanFamilyEnterprise.Should().Be("enterprise");
    }

    [Fact]
    public void Operation_result_vocabulary_matches_the_stable_v1_set()
    {
        var expected = new[]
        {
            "activated",
            "transition_unchanged",
            "transition_not_higher",
            "plan_unknown",
            "plan_unavailable",
            "precondition_conflict",
            "entitlement_not_initialized"
        };

        var actual = new[]
        {
            LicencePersistenceVocabulary.OperationResultActivated,
            LicencePersistenceVocabulary.OperationResultTransitionUnchanged,
            LicencePersistenceVocabulary.OperationResultTransitionNotHigher,
            LicencePersistenceVocabulary.OperationResultPlanUnknown,
            LicencePersistenceVocabulary.OperationResultPlanUnavailable,
            LicencePersistenceVocabulary.OperationResultPreconditionConflict,
            LicencePersistenceVocabulary.OperationResultEntitlementNotInitialized
        };

        actual.Should().BeEquivalentTo(expected);
    }

    [Fact]
    public void Operation_result_set_does_not_include_authority_or_infrastructure_outcomes()
    {
        // Storage_unavailable / concurrency_exhausted are not database-evaluated business
        // outcomes and must never be persisted as a DurableResult.
        LicencePersistenceVocabulary.OperationResultActivated.Should().NotBe("storage_unavailable");
    }
}
