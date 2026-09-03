using HushNode.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace HushNode.HushVoting.Licensing.Storage;

/// <summary>
/// Contributes the HushVoting licensing persistence model to the unified
/// <c>HushNodeDbContext</c> under the <c>HushVoting</c> schema. All schema changes flow
/// through the single operational migration stream in <c>HushServerNode/Migrations</c>.
/// </summary>
public sealed class LicensingDbContextConfigurator : IDbContextConfigurator
{
    public const string SchemaName = "HushVoting";

    public void Configure(ModelBuilder modelBuilder)
    {
        ConfigureCatalogueRelease(modelBuilder);
        ConfigureSubject(modelBuilder);
        ConfigureAssignment(modelBuilder);
        ConfigureTransitionEvent(modelBuilder);
        ConfigureActivationOperation(modelBuilder);
    }

    private static void ConfigureCatalogueRelease(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LicenceCatalogueReleaseEntity>();
        entity.ToTable("LicenceCatalogueRelease", SchemaName, table =>
        {
            table.HasCheckConstraint("CK_LicenceCatalogueRelease_DigestFormat",
                "char_length(\"ReleaseDigestSha256\") = 64");
            table.HasCheckConstraint("CK_LicenceCatalogueRelease_WatermarkNonNegative",
                "\"RolloutWatermarkBlockHeight\" IS NULL OR \"RolloutWatermarkBlockHeight\" >= 0");
        });

        entity.HasKey(x => x.LicenceCatalogueReleaseId);
        entity.Property(x => x.CatalogueVersion).HasColumnType("varchar(96)").IsRequired();
        entity.Property(x => x.ReleaseDigestSha256).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.SchemaVersion).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.InstalledByServerRelease).HasColumnType("varchar(160)").IsRequired();
        entity.Property(x => x.InstalledByServerHost).HasColumnType("varchar(160)").IsRequired();
        entity.Property(x => x.InstalledAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.IsCurrent).IsRequired();
        entity.Property(x => x.RolloutWatermarkBlockHeight).HasColumnType("bigint");

        // Append-only ledger: a version+digest pair is unique; replay is idempotent.
        entity.HasIndex(x => new { x.CatalogueVersion, x.ReleaseDigestSha256 })
            .IsUnique()
            .HasDatabaseName("IX_LicenceCatalogueRelease_Version_Digest");

        // Exactly one current release at a time.
        entity.HasIndex(x => x.IsCurrent)
            .IsUnique()
            .HasFilter("\"IsCurrent\" = TRUE")
            .HasDatabaseName("IX_LicenceCatalogueRelease_SingleCurrent");
    }

    private static void ConfigureSubject(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LicenceSubjectEntity>();
        entity.ToTable("LicenceSubject", SchemaName, table =>
        {
            table.HasCheckConstraint("CK_LicenceSubject_SubjectType",
                "\"SubjectType\" IN ('Identity')");
            table.HasCheckConstraint("CK_LicenceSubject_AddressNotEmpty",
                "char_length(\"CanonicalPublicSigningAddress\") > 0");
            table.HasCheckConstraint("CK_LicenceSubject_RevisionNonNegative",
                "\"EntitlementRevision\" >= 0");
            table.HasCheckConstraint("CK_LicenceSubject_CreationBlockNonNegative",
                "\"IdentityCreationBlockIndex\" >= 0");
        });

        entity.HasKey(x => x.LicenceSubjectId);
        entity.Property(x => x.SubjectType).HasColumnType("varchar(32)").IsRequired();
        entity.Property(x => x.CanonicalPublicSigningAddress).HasColumnType("varchar(160)").IsRequired();
        entity.Property(x => x.IdentityCreationBlockIndex).HasColumnType("bigint").IsRequired();
        entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.EntitlementRevision).HasColumnType("bigint").IsRequired();

        // One subject per canonical identity of a given subject type.
        entity.HasIndex(x => new { x.SubjectType, x.CanonicalPublicSigningAddress })
            .IsUnique()
            .HasDatabaseName("IX_LicenceSubject_Type_CanonicalAddress");
    }

    private static void ConfigureAssignment(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LicenceAssignmentEntity>();
        entity.ToTable("LicenceAssignment", SchemaName, table =>
        {
            table.HasCheckConstraint("CK_LicenceAssignment_LifecycleStatus",
                "\"LifecycleStatus\" IN ('active', 'superseded', 'expired')");
            table.HasCheckConstraint("CK_LicenceAssignment_Source",
                "\"Source\" IN ('default_free', 'migration_lazy_default', 'automatic_upgrade', 'automatic_expiry')");
            table.HasCheckConstraint("CK_LicenceAssignment_PlanFamily",
                "\"PlanFamily\" IN ('direct', 'veritas', 'enterprise')");
            table.HasCheckConstraint("CK_LicenceAssignment_TermKind",
                "\"TermKind\" IN ('perpetual', 'annual')");
            table.HasCheckConstraint("CK_LicenceAssignment_AnnualHasExpiry",
                "\"TermKind\" <> 'annual' OR \"ExpiresAtUtc\" IS NOT NULL");
            table.HasCheckConstraint("CK_LicenceAssignment_PerpetualNoExpiry",
                "\"TermKind\" <> 'perpetual' OR \"ExpiresAtUtc\" IS NULL");
            table.HasCheckConstraint("CK_LicenceAssignment_IntervalOrder",
                "\"ExpiresAtUtc\" IS NULL OR \"EffectiveFromUtc\" < \"ExpiresAtUtc\"");
            table.HasCheckConstraint("CK_LicenceAssignment_TermYears",
                "((\"TermKind\" = 'perpetual' AND \"TermYears\" = 0) OR (\"TermKind\" = 'annual' AND \"TermYears\" = 1))");
            table.HasCheckConstraint("CK_LicenceAssignment_CapPositive",
                "\"EligibleVoterCap\" IS NULL OR \"EligibleVoterCap\" > 0");
            table.HasCheckConstraint("CK_LicenceAssignment_UpgradeRankNonNegative",
                "\"UpgradeRank\" >= 0");
            table.HasCheckConstraint("CK_LicenceAssignment_LifecycleChangedPair",
                "((\"LifecycleStatus\" = 'active' AND \"LifecycleChangedAtUtc\" IS NULL AND \"LifecycleReason\" IS NULL) OR (\"LifecycleStatus\" IN ('superseded', 'expired') AND \"LifecycleChangedAtUtc\" IS NOT NULL AND \"LifecycleReason\" IS NOT NULL))");
            table.HasCheckConstraint("CK_LicenceAssignment_EffectiveFromNotBackdated",
                "\"EffectiveFromUtc\" >= '2020-01-01T00:00:00Z'");
        });

        entity.HasKey(x => x.LicenceAssignmentId);
        entity.Property(x => x.PlanId).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.AssignedCatalogueVersion).HasColumnType("varchar(96)").IsRequired();
        entity.Property(x => x.AssignedCatalogueDigestSha256).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.LifecycleStatus).HasColumnType("varchar(16)").IsRequired();
        entity.Property(x => x.Source).HasColumnType("varchar(32)").IsRequired();
        entity.Property(x => x.EffectiveFromUtc).HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.ExpiresAtUtc).HasColumnType("timestamp with time zone");
        entity.Property(x => x.LifecycleChangedAtUtc).HasColumnType("timestamp with time zone");
        entity.Property(x => x.LifecycleReason).HasColumnType("varchar(96)");
        entity.Property(x => x.PlanFamily).HasColumnType("varchar(16)").IsRequired();
        entity.Property(x => x.UpgradeRank).HasColumnType("integer").IsRequired();
        entity.Property(x => x.EligibleVoterCap).HasColumnType("integer");
        entity.Property(x => x.UnlimitedElectionPolicy).HasColumnType("boolean").IsRequired();
        entity.Property(x => x.TermKind).HasColumnType("varchar(16)").IsRequired();
        entity.Property(x => x.TermYears).HasColumnType("integer").IsRequired();
        entity.Property(x => x.AllowedGovernanceOptionIds).HasColumnType("text[]").IsRequired();
        entity.Property(x => x.CreationCorrelationId).HasColumnType("varchar(96)");
        entity.Property(x => x.CreatedByOperationId).HasColumnType("uuid");

        entity.HasOne(x => x.LicenceSubject)
            .WithMany(x => x.Assignments)
            .HasForeignKey(x => x.LicenceSubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(x => x.CreatedByOperation)
            .WithMany()
            .HasForeignKey(x => x.CreatedByOperationId)
            .OnDelete(DeleteBehavior.Restrict);

        // At most one ACTIVE assignment per subject.
        entity.HasIndex(x => x.LicenceSubjectId)
            .IsUnique()
            .HasFilter("\"LifecycleStatus\" = 'active'")
            .HasDatabaseName("IX_LicenceAssignment_SingleActivePerSubject");

        entity.HasIndex(x => new { x.LicenceSubjectId, x.LifecycleStatus })
            .HasDatabaseName("IX_LicenceAssignment_Subject_Lifecycle");

        // Due-expiry index for future operations (no background scheduler in FEAT-013).
        entity.HasIndex(x => new { x.LifecycleStatus, x.ExpiresAtUtc })
            .HasDatabaseName("IX_LicenceAssignment_DueExpiry");
    }

    private static void ConfigureTransitionEvent(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LicenceTransitionEventEntity>();
        entity.ToTable("LicenceTransitionEvent", SchemaName, table =>
        {
            table.HasCheckConstraint("CK_LicenceTransitionEvent_EventType",
                "\"EventType\" IN ('created', 'superseded', 'expired')");
            table.HasCheckConstraint("CK_LicenceTransitionEvent_SequencePositive",
                "\"EventSequence\" > 0");
            table.HasCheckConstraint("CK_LicenceTransitionEvent_RevisionNonNegative",
                "\"SubjectRevision\" >= 0");
        });

        entity.HasKey(x => x.LicenceTransitionEventId);
        entity.Property(x => x.EventSequence).HasColumnType("bigint").IsRequired();
        entity.Property(x => x.EventType).HasColumnType("varchar(16)").IsRequired();
        entity.Property(x => x.SubjectRevision).HasColumnType("bigint").IsRequired();
        entity.Property(x => x.PlanId).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.CatalogueDecisionVersion).HasColumnType("varchar(96)").IsRequired();
        entity.Property(x => x.SourceOrReason).HasColumnType("varchar(96)");
        entity.Property(x => x.OperationReferenceId).HasColumnType("uuid");
        entity.Property(x => x.OccurredAtUtc).HasColumnType("timestamp with time zone").IsRequired();

        entity.HasOne(x => x.LicenceSubject)
            .WithMany(x => x.TransitionEvents)
            .HasForeignKey(x => x.LicenceSubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(x => x.Assignment)
            .WithMany()
            .HasForeignKey(x => x.AssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Monotonic per-subject event ordering.
        entity.HasIndex(x => new { x.LicenceSubjectId, x.EventSequence })
            .IsUnique()
            .HasDatabaseName("IX_LicenceTransitionEvent_Subject_Sequence");
    }

    private static void ConfigureActivationOperation(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<LicenceActivationOperationEntity>();
        entity.ToTable("LicenceActivationOperation", SchemaName, table =>
        {
            table.HasCheckConstraint("CK_LicenceActivationOperation_FingerprintFormat",
                "char_length(\"CanonicalPayloadFingerprintSha256\") = 64");
            table.HasCheckConstraint("CK_LicenceActivationOperation_ExpectedRevisionNonNegative",
                "\"ExpectedEntitlementRevision\" >= 0");
            table.HasCheckConstraint("CK_LicenceActivationOperation_DurableResult",
                "\"DurableResult\" IS NULL OR \"DurableResult\" IN ('activated', 'transition_unchanged', 'transition_not_higher', 'plan_unknown', 'plan_unavailable', 'precondition_conflict', 'entitlement_not_initialized')");
            table.HasCheckConstraint("CK_LicenceActivationOperation_CompletedPair",
                "((\"DurableResult\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"ResultingAssignmentId\" IS NULL AND \"ResultingEntitlementRevision\" IS NULL) OR (\"DurableResult\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL))");
            table.HasCheckConstraint("CK_LicenceActivationOperation_ResultingOnlyWhenActivated",
                "\"ResultingAssignmentId\" IS NULL OR \"DurableResult\" = 'activated'");
        });

        entity.HasKey(x => x.LicenceActivationOperationId);
        entity.Property(x => x.IdempotencyKey).HasColumnType("uuid").IsRequired();
        entity.Property(x => x.CanonicalPayloadFingerprintSha256).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.ExpectedCurrentPlanId).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.ExpectedEntitlementRevision).HasColumnType("bigint").IsRequired();
        entity.Property(x => x.RequestedTargetPlanId).HasColumnType("varchar(64)").IsRequired();
        entity.Property(x => x.EvaluatedCatalogueVersion).HasColumnType("varchar(96)").IsRequired();
        entity.Property(x => x.DurableResult).HasColumnType("varchar(48)");
        entity.Property(x => x.ResultingAssignmentId).HasColumnType("uuid");
        entity.Property(x => x.ResultingEntitlementRevision).HasColumnType("bigint");
        entity.Property(x => x.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        entity.Property(x => x.CompletedAtUtc).HasColumnType("timestamp with time zone");
        entity.Property(x => x.RequestCorrelationId).HasColumnType("varchar(96)");

        entity.HasOne(x => x.LicenceSubject)
            .WithMany(x => x.ActivationOperations)
            .HasForeignKey(x => x.LicenceSubjectId)
            .OnDelete(DeleteBehavior.Restrict);

        entity.HasOne(x => x.ResultingAssignment)
            .WithOne()
            .HasForeignKey<LicenceActivationOperationEntity>(x => x.ResultingAssignmentId)
            .OnDelete(DeleteBehavior.Restrict);

        // Idempotency key uniqueness is scoped per licence subject.
        entity.HasIndex(x => new { x.LicenceSubjectId, x.IdempotencyKey })
            .IsUnique()
            .HasDatabaseName("IX_LicenceActivationOperation_Subject_IdempotencyKey");

        entity.HasIndex(x => x.LicenceSubjectId)
            .HasDatabaseName("IX_LicenceActivationOperation_Subject");
    }
}
