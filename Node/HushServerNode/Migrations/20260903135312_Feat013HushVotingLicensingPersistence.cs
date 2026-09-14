using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat013HushVotingLicensingPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "HushVoting");

            migrationBuilder.CreateTable(
                name: "LicenceCatalogueRelease",
                schema: "HushVoting",
                columns: table => new
                {
                    LicenceCatalogueReleaseId = table.Column<Guid>(type: "uuid", nullable: false),
                    CatalogueVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    ReleaseDigestSha256 = table.Column<string>(type: "varchar(64)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    InstalledByServerRelease = table.Column<string>(type: "varchar(160)", nullable: false),
                    InstalledByServerHost = table.Column<string>(type: "varchar(160)", nullable: false),
                    InstalledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    RolloutWatermarkBlockHeight = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenceCatalogueRelease", x => x.LicenceCatalogueReleaseId);
                    table.CheckConstraint("CK_LicenceCatalogueRelease_DigestFormat", "char_length(\"ReleaseDigestSha256\") = 64");
                    table.CheckConstraint("CK_LicenceCatalogueRelease_WatermarkNonNegative", "\"RolloutWatermarkBlockHeight\" IS NULL OR \"RolloutWatermarkBlockHeight\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "LicenceSubject",
                schema: "HushVoting",
                columns: table => new
                {
                    LicenceSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectType = table.Column<string>(type: "varchar(32)", nullable: false),
                    CanonicalPublicSigningAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    IdentityCreationBlockIndex = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EntitlementRevision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenceSubject", x => x.LicenceSubjectId);
                    table.CheckConstraint("CK_LicenceSubject_AddressNotEmpty", "char_length(\"CanonicalPublicSigningAddress\") > 0");
                    table.CheckConstraint("CK_LicenceSubject_CreationBlockNonNegative", "\"IdentityCreationBlockIndex\" >= 0");
                    table.CheckConstraint("CK_LicenceSubject_RevisionNonNegative", "\"EntitlementRevision\" >= 0");
                    table.CheckConstraint("CK_LicenceSubject_SubjectType", "\"SubjectType\" IN ('Identity')");
                });

            migrationBuilder.CreateTable(
                name: "LicenceActivationOperation",
                schema: "HushVoting",
                columns: table => new
                {
                    LicenceActivationOperationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenceSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalPayloadFingerprintSha256 = table.Column<string>(type: "varchar(64)", nullable: false),
                    ExpectedCurrentPlanId = table.Column<string>(type: "varchar(64)", nullable: false),
                    ExpectedEntitlementRevision = table.Column<long>(type: "bigint", nullable: false),
                    RequestedTargetPlanId = table.Column<string>(type: "varchar(64)", nullable: false),
                    EvaluatedCatalogueVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    DurableResult = table.Column<string>(type: "varchar(48)", nullable: true),
                    ResultingAssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResultingEntitlementRevision = table.Column<long>(type: "bigint", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestCorrelationId = table.Column<string>(type: "varchar(96)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenceActivationOperation", x => x.LicenceActivationOperationId);
                    table.CheckConstraint("CK_LicenceActivationOperation_CompletedPair", "((\"DurableResult\" IS NULL AND \"CompletedAtUtc\" IS NULL AND \"ResultingAssignmentId\" IS NULL AND \"ResultingEntitlementRevision\" IS NULL) OR (\"DurableResult\" IS NOT NULL AND \"CompletedAtUtc\" IS NOT NULL))");
                    table.CheckConstraint("CK_LicenceActivationOperation_DurableResult", "\"DurableResult\" IS NULL OR \"DurableResult\" IN ('activated', 'transition_unchanged', 'transition_not_higher', 'plan_unknown', 'plan_unavailable', 'precondition_conflict', 'entitlement_not_initialized')");
                    table.CheckConstraint("CK_LicenceActivationOperation_ExpectedRevisionNonNegative", "\"ExpectedEntitlementRevision\" >= 0");
                    table.CheckConstraint("CK_LicenceActivationOperation_FingerprintFormat", "char_length(\"CanonicalPayloadFingerprintSha256\") = 64");
                    table.CheckConstraint("CK_LicenceActivationOperation_ResultingOnlyWhenActivated", "\"ResultingAssignmentId\" IS NULL OR \"DurableResult\" = 'activated'");
                    table.ForeignKey(
                        name: "FK_LicenceActivationOperation_LicenceSubject_LicenceSubjectId",
                        column: x => x.LicenceSubjectId,
                        principalSchema: "HushVoting",
                        principalTable: "LicenceSubject",
                        principalColumn: "LicenceSubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenceAssignment",
                schema: "HushVoting",
                columns: table => new
                {
                    LicenceAssignmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenceSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<string>(type: "varchar(64)", nullable: false),
                    AssignedCatalogueVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    AssignedCatalogueDigestSha256 = table.Column<string>(type: "varchar(64)", nullable: false),
                    LifecycleStatus = table.Column<string>(type: "varchar(16)", nullable: false),
                    Source = table.Column<string>(type: "varchar(32)", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LifecycleChangedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LifecycleReason = table.Column<string>(type: "varchar(96)", nullable: true),
                    PlanFamily = table.Column<string>(type: "varchar(16)", nullable: false),
                    UpgradeRank = table.Column<int>(type: "integer", nullable: false),
                    EligibleVoterCap = table.Column<int>(type: "integer", nullable: true),
                    UnlimitedElectionPolicy = table.Column<bool>(type: "boolean", nullable: false),
                    TermKind = table.Column<string>(type: "varchar(16)", nullable: false),
                    TermYears = table.Column<int>(type: "integer", nullable: false),
                    AllowedGovernanceOptionIds = table.Column<string[]>(type: "text[]", nullable: false),
                    CreationCorrelationId = table.Column<string>(type: "varchar(96)", nullable: true),
                    CreatedByOperationId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenceAssignment", x => x.LicenceAssignmentId);
                    table.CheckConstraint("CK_LicenceAssignment_AnnualHasExpiry", "\"TermKind\" <> 'annual' OR \"ExpiresAtUtc\" IS NOT NULL");
                    table.CheckConstraint("CK_LicenceAssignment_CapPositive", "\"EligibleVoterCap\" IS NULL OR \"EligibleVoterCap\" > 0");
                    table.CheckConstraint("CK_LicenceAssignment_EffectiveFromNotBackdated", "\"EffectiveFromUtc\" >= '2020-01-01T00:00:00Z'");
                    table.CheckConstraint("CK_LicenceAssignment_IntervalOrder", "\"ExpiresAtUtc\" IS NULL OR \"EffectiveFromUtc\" < \"ExpiresAtUtc\"");
                    table.CheckConstraint("CK_LicenceAssignment_LifecycleChangedPair", "((\"LifecycleStatus\" = 'active' AND \"LifecycleChangedAtUtc\" IS NULL AND \"LifecycleReason\" IS NULL) OR (\"LifecycleStatus\" IN ('superseded', 'expired') AND \"LifecycleChangedAtUtc\" IS NOT NULL AND \"LifecycleReason\" IS NOT NULL))");
                    table.CheckConstraint("CK_LicenceAssignment_LifecycleStatus", "\"LifecycleStatus\" IN ('active', 'superseded', 'expired')");
                    table.CheckConstraint("CK_LicenceAssignment_PerpetualNoExpiry", "\"TermKind\" <> 'perpetual' OR \"ExpiresAtUtc\" IS NULL");
                    table.CheckConstraint("CK_LicenceAssignment_PlanFamily", "\"PlanFamily\" IN ('direct', 'veritas', 'enterprise')");
                    table.CheckConstraint("CK_LicenceAssignment_Source", "\"Source\" IN ('default_free', 'migration_lazy_default', 'automatic_upgrade', 'automatic_expiry')");
                    table.CheckConstraint("CK_LicenceAssignment_TermKind", "\"TermKind\" IN ('perpetual', 'annual')");
                    table.CheckConstraint("CK_LicenceAssignment_TermYears", "((\"TermKind\" = 'perpetual' AND \"TermYears\" = 0) OR (\"TermKind\" = 'annual' AND \"TermYears\" = 1))");
                    table.CheckConstraint("CK_LicenceAssignment_UpgradeRankNonNegative", "\"UpgradeRank\" >= 0");
                    table.ForeignKey(
                        name: "FK_LicenceAssignment_LicenceActivationOperation_CreatedByOpera~",
                        column: x => x.CreatedByOperationId,
                        principalSchema: "HushVoting",
                        principalTable: "LicenceActivationOperation",
                        principalColumn: "LicenceActivationOperationId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenceAssignment_LicenceSubject_LicenceSubjectId",
                        column: x => x.LicenceSubjectId,
                        principalSchema: "HushVoting",
                        principalTable: "LicenceSubject",
                        principalColumn: "LicenceSubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "LicenceTransitionEvent",
                schema: "HushVoting",
                columns: table => new
                {
                    LicenceTransitionEventId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenceSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventSequence = table.Column<long>(type: "bigint", nullable: false),
                    EventType = table.Column<string>(type: "varchar(16)", nullable: false),
                    SubjectRevision = table.Column<long>(type: "bigint", nullable: false),
                    AssignmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    PlanId = table.Column<string>(type: "varchar(64)", nullable: false),
                    CatalogueDecisionVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    SourceOrReason = table.Column<string>(type: "varchar(96)", nullable: true),
                    OperationReferenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenceTransitionEvent", x => x.LicenceTransitionEventId);
                    table.CheckConstraint("CK_LicenceTransitionEvent_EventType", "\"EventType\" IN ('created', 'superseded', 'expired')");
                    table.CheckConstraint("CK_LicenceTransitionEvent_RevisionNonNegative", "\"SubjectRevision\" >= 0");
                    table.CheckConstraint("CK_LicenceTransitionEvent_SequencePositive", "\"EventSequence\" > 0");
                    table.ForeignKey(
                        name: "FK_LicenceTransitionEvent_LicenceAssignment_AssignmentId",
                        column: x => x.AssignmentId,
                        principalSchema: "HushVoting",
                        principalTable: "LicenceAssignment",
                        principalColumn: "LicenceAssignmentId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LicenceTransitionEvent_LicenceSubject_LicenceSubjectId",
                        column: x => x.LicenceSubjectId,
                        principalSchema: "HushVoting",
                        principalTable: "LicenceSubject",
                        principalColumn: "LicenceSubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenceActivationOperation_ResultingAssignmentId",
                schema: "HushVoting",
                table: "LicenceActivationOperation",
                column: "ResultingAssignmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenceActivationOperation_Subject",
                schema: "HushVoting",
                table: "LicenceActivationOperation",
                column: "LicenceSubjectId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceActivationOperation_Subject_IdempotencyKey",
                schema: "HushVoting",
                table: "LicenceActivationOperation",
                columns: new[] { "LicenceSubjectId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenceAssignment_CreatedByOperationId",
                schema: "HushVoting",
                table: "LicenceAssignment",
                column: "CreatedByOperationId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceAssignment_DueExpiry",
                schema: "HushVoting",
                table: "LicenceAssignment",
                columns: new[] { "LifecycleStatus", "ExpiresAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenceAssignment_SingleActivePerSubject",
                schema: "HushVoting",
                table: "LicenceAssignment",
                column: "LicenceSubjectId",
                unique: true,
                filter: "\"LifecycleStatus\" = 'active'");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceAssignment_Subject_Lifecycle",
                schema: "HushVoting",
                table: "LicenceAssignment",
                columns: new[] { "LicenceSubjectId", "LifecycleStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_LicenceCatalogueRelease_SingleCurrent",
                schema: "HushVoting",
                table: "LicenceCatalogueRelease",
                column: "IsCurrent",
                unique: true,
                filter: "\"IsCurrent\" = TRUE");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceCatalogueRelease_Version_Digest",
                schema: "HushVoting",
                table: "LicenceCatalogueRelease",
                columns: new[] { "CatalogueVersion", "ReleaseDigestSha256" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenceSubject_Type_CanonicalAddress",
                schema: "HushVoting",
                table: "LicenceSubject",
                columns: new[] { "SubjectType", "CanonicalPublicSigningAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenceTransitionEvent_AssignmentId",
                schema: "HushVoting",
                table: "LicenceTransitionEvent",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceTransitionEvent_Subject_Sequence",
                schema: "HushVoting",
                table: "LicenceTransitionEvent",
                columns: new[] { "LicenceSubjectId", "EventSequence" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_LicenceActivationOperation_LicenceAssignment_ResultingAssig~",
                schema: "HushVoting",
                table: "LicenceActivationOperation",
                column: "ResultingAssignmentId",
                principalSchema: "HushVoting",
                principalTable: "LicenceAssignment",
                principalColumn: "LicenceAssignmentId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Destructive rollback guard (AC-013-017): refuse while any licensing history row
            // that cannot be reconstructed exists. Production recovery uses a forward-fix migration.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "HushVoting"."LicenceCatalogueRelease") THEN
                        RAISE EXCEPTION 'Destructive rollback refused: HushVoting.LicenceCatalogueRelease is not empty; use a forward-fix migration.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "HushVoting"."LicenceSubject") THEN
                        RAISE EXCEPTION 'Destructive rollback refused: HushVoting.LicenceSubject is not empty; use a forward-fix migration.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "HushVoting"."LicenceAssignment") THEN
                        RAISE EXCEPTION 'Destructive rollback refused: HushVoting.LicenceAssignment is not empty; use a forward-fix migration.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "HushVoting"."LicenceTransitionEvent") THEN
                        RAISE EXCEPTION 'Destructive rollback refused: HushVoting.LicenceTransitionEvent is not empty; use a forward-fix migration.';
                    END IF;
                    IF EXISTS (SELECT 1 FROM "HushVoting"."LicenceActivationOperation") THEN
                        RAISE EXCEPTION 'Destructive rollback refused: HushVoting.LicenceActivationOperation is not empty; use a forward-fix migration.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_LicenceActivationOperation_LicenceAssignment_ResultingAssig~",
                schema: "HushVoting",
                table: "LicenceActivationOperation");

            migrationBuilder.DropTable(
                name: "LicenceCatalogueRelease",
                schema: "HushVoting");

            migrationBuilder.DropTable(
                name: "LicenceTransitionEvent",
                schema: "HushVoting");

            migrationBuilder.DropTable(
                name: "LicenceAssignment",
                schema: "HushVoting");

            migrationBuilder.DropTable(
                name: "LicenceActivationOperation",
                schema: "HushVoting");

            migrationBuilder.DropTable(
                name: "LicenceSubject",
                schema: "HushVoting");

            // The licensing schema is removed only when every licensing table was empty.
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS \"HushVoting\"");
        }
    }
}
