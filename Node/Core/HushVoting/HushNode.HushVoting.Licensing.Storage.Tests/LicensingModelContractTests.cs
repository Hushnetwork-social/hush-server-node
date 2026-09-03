using FluentAssertions;
using HushNode.HushVoting.Licensing.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace HushNode.HushVoting.Licensing.Storage.Tests;

/// <summary>
/// Model-contract tests: assert the EF model metadata (tables, columns, keys, indexes,
/// check constraints, relationships, privacy exclusions) matches the FEAT-013 contract.
/// These are metadata assertions over the model definition — no EF InMemory or SQLite is
/// used, and relational behavior is proven against real PostgreSQL in the TwinTests.
/// </summary>
public class LicensingModelContractTests
{
    private static IModel BuildModel()
    {
        var builder = new ModelBuilder();
        new LicensingDbContextConfigurator().Configure(builder);
        return (IModel)builder.Model;
    }

    private static IEntityType Entity(Type clrType)
        => BuildModel().GetEntityTypes().Single(e => e.ClrType == clrType);

    private static IEntityType Entity<T>() => Entity(typeof(T));

    [Fact]
    public void All_five_record_families_are_mapped_to_the_HushVoting_schema()
    {
        var model = BuildModel();
        model.GetEntityTypes().Select(e => e.ClrType)
            .Should().Contain(new[]
            {
                typeof(LicenceCatalogueReleaseEntity),
                typeof(LicenceSubjectEntity),
                typeof(LicenceAssignmentEntity),
                typeof(LicenceTransitionEventEntity),
                typeof(LicenceActivationOperationEntity)
            });

        foreach (var entityType in model.GetEntityTypes())
        {
            entityType.GetSchema().Should().Be("HushVoting");
            entityType.GetTableName().Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Subject_has_canonical_address_and_identity_creation_block()
    {
        var subject = Entity<LicenceSubjectEntity>();

        subject.GetProperty(nameof(LicenceSubjectEntity.CanonicalPublicSigningAddress))
            .GetColumnType().Should().Be("varchar(160)");
        subject.GetProperty(nameof(LicenceSubjectEntity.IdentityCreationBlockIndex))
            .GetColumnType().Should().Be("bigint");
        subject.GetProperty(nameof(LicenceSubjectEntity.EntitlementRevision))
            .GetColumnType().Should().Be("bigint");
        subject.GetProperty(nameof(LicenceSubjectEntity.SubjectType))
            .GetColumnType().Should().Be("varchar(32)");
    }

    [Fact]
    public void Raw_signing_address_exists_only_on_the_subject_row()
    {
        var guardedTypes = new[]
        {
            typeof(LicenceAssignmentEntity),
            typeof(LicenceTransitionEventEntity),
            typeof(LicenceActivationOperationEntity),
            typeof(LicenceCatalogueReleaseEntity)
        };

        foreach (var type in guardedTypes)
        {
            Entity(type).GetProperties()
                .Select(p => p.Name)
                .Should().NotContain(nameof(LicenceSubjectEntity.CanonicalPublicSigningAddress),
                    because: "the raw signing address must never be repeated off the subject row");
        }
    }

    [Fact]
    public void Subject_unique_constraint_is_per_canonical_identity()
    {
        var subject = Entity<LicenceSubjectEntity>();
        var index = subject.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(LicenceSubjectEntity.SubjectType),
                nameof(LicenceSubjectEntity.CanonicalPublicSigningAddress)
            }));
        index.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Assignment_has_single_active_partial_unique_index()
    {
        var assignment = Entity<LicenceAssignmentEntity>();
        var index = assignment.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[] { nameof(LicenceAssignmentEntity.LicenceSubjectId) })
            && i.IsUnique);
        index.GetFilter().Should().Contain("'active'");
    }

    [Fact]
    public void Assignment_has_due_expiry_and_lifecycle_indexes()
    {
        var assignment = Entity<LicenceAssignmentEntity>();
        var names = assignment.GetIndexes().Select(i => i.GetDatabaseName()).ToList();
        names.Should().Contain("IX_LicenceAssignment_DueExpiry", "IX_LicenceAssignment_Subject_Lifecycle");
    }

    [Fact]
    public void Transition_events_are_ordered_per_subject()
    {
        var evt = Entity<LicenceTransitionEventEntity>();
        var index = evt.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(LicenceTransitionEventEntity.LicenceSubjectId),
                nameof(LicenceTransitionEventEntity.EventSequence)
            }));
        index.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Activation_idempotency_is_unique_per_subject()
    {
        var op = Entity<LicenceActivationOperationEntity>();
        var index = op.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(LicenceActivationOperationEntity.LicenceSubjectId),
                nameof(LicenceActivationOperationEntity.IdempotencyKey)
            }));
        index.IsUnique.Should().BeTrue();
    }

    [Fact]
    public void Catalogue_release_ledger_allows_one_current_and_unique_version_digest()
    {
        var release = Entity<LicenceCatalogueReleaseEntity>();
        release.GetIndexes().Single(i => i.GetDatabaseName() == "IX_LicenceCatalogueRelease_Version_Digest")
            .IsUnique.Should().BeTrue();
        var singleCurrent = release.GetIndexes().Single(i => i.GetDatabaseName() == "IX_LicenceCatalogueRelease_SingleCurrent");
        singleCurrent.IsUnique.Should().BeTrue();
        singleCurrent.GetFilter().Should().Contain("TRUE");
    }

    [Fact]
    public void Check_constraints_cover_closed_vocabularies_and_intervals()
    {
        var model = BuildModel();
        var constraintNames = model.GetEntityTypes()
            .SelectMany(RelationalEntityTypeExtensions.GetCheckConstraints)
            .Select(c => c.Name)
            .ToList();

        constraintNames.Should().Contain(new[]
        {
            "CK_LicenceSubject_SubjectType",
            "CK_LicenceSubject_AddressNotEmpty",
            "CK_LicenceAssignment_LifecycleStatus",
            "CK_LicenceAssignment_Source",
            "CK_LicenceAssignment_AnnualHasExpiry",
            "CK_LicenceAssignment_PerpetualNoExpiry",
            "CK_LicenceAssignment_IntervalOrder",
            "CK_LicenceAssignment_TermYears",
            "CK_LicenceAssignment_LifecycleChangedPair",
            "CK_LicenceTransitionEvent_EventType",
            "CK_LicenceTransitionEvent_SequencePositive",
            "CK_LicenceActivationOperation_DurableResult",
            "CK_LicenceActivationOperation_CompletedPair"
        });
    }

    [Fact]
    public void Foreign_keys_use_restrict_semantics_no_destructive_cascade()
    {
        var model = BuildModel();
        foreach (var fk in model.GetEntityTypes().SelectMany(e => e.GetForeignKeys()))
        {
            fk.DeleteBehavior.Should().Be(DeleteBehavior.Restrict);
        }
    }

    [Fact]
    public void Utc_instants_are_timestamp_with_time_zone_columns()
    {
        var subject = Entity<LicenceSubjectEntity>();
        subject.GetProperty(nameof(LicenceSubjectEntity.CreatedAtUtc))
            .GetColumnType().Should().Be("timestamp with time zone");

        var assignment = Entity<LicenceAssignmentEntity>();
        assignment.GetProperty(nameof(LicenceAssignmentEntity.EffectiveFromUtc))
            .GetColumnType().Should().Be("timestamp with time zone");
        assignment.GetProperty(nameof(LicenceAssignmentEntity.ExpiresAtUtc))
            .GetColumnType().Should().Be("timestamp with time zone");
    }

    [Fact]
    public void Internal_identifiers_are_guid_keys()
    {
        foreach (var entityType in BuildModel().GetEntityTypes())
        {
            var key = entityType.FindPrimaryKey();
            key.Should().NotBeNull();
            key!.Properties.Should().HaveCount(1);
            key.Properties[0].ClrType.Should().Be(typeof(Guid));
        }
    }
}
