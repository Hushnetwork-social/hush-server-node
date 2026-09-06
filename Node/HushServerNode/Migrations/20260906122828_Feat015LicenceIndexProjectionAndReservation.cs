using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat015LicenceIndexProjectionAndReservation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_LicenceAssignment_Source",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.AddColumn<long>(
                name: "OriginatingBlockIndex",
                schema: "HushVoting",
                table: "LicenceAssignment",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "OriginatingBlockTimeStampUtc",
                schema: "HushVoting",
                table: "LicenceAssignment",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "OriginatingTransactionId",
                schema: "HushVoting",
                table: "LicenceAssignment",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByAssignmentId",
                schema: "HushVoting",
                table: "LicenceAssignment",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LicencePendingReservation",
                schema: "HushVoting",
                columns: table => new
                {
                    LicencePendingReservationId = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenceSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginatingTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalPayloadFingerprintSha256 = table.Column<string>(type: "varchar(64)", nullable: false),
                    TransitionIntent = table.Column<string>(type: "varchar(32)", nullable: false),
                    RequestedPlanId = table.Column<string>(type: "varchar(64)", nullable: false),
                    ObservedCatalogueVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    ExpectedCurrentLicenceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ExpectedCurrentPlanId = table.Column<string>(type: "varchar(64)", nullable: true),
                    LifecycleStatus = table.Column<string>(type: "varchar(16)", nullable: false),
                    RequestedUpgradeRank = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicencePendingReservation", x => x.LicencePendingReservationId);
                    table.CheckConstraint("CK_LicencePendingReservation_BaselineNoExpectedCurrent", "((\"TransitionIntent\" = 'baseline_free' AND \"ExpectedCurrentLicenceTransactionId\" IS NULL AND \"ExpectedCurrentPlanId\" IS NULL) OR (\"TransitionIntent\" = 'confirmed_upgrade' AND \"ExpectedCurrentLicenceTransactionId\" IS NOT NULL AND \"ExpectedCurrentPlanId\" IS NOT NULL))");
                    table.CheckConstraint("CK_LicencePendingReservation_FingerprintFormat", "char_length(\"CanonicalPayloadFingerprintSha256\") = 64");
                    table.CheckConstraint("CK_LicencePendingReservation_Intent", "\"TransitionIntent\" IN ('baseline_free', 'confirmed_upgrade')");
                    table.CheckConstraint("CK_LicencePendingReservation_Lifecycle", "\"LifecycleStatus\" IN ('pending', 'superseded', 'resolved')");
                    table.CheckConstraint("CK_LicencePendingReservation_RankNonNegative", "\"RequestedUpgradeRank\" >= 0");
                    table.CheckConstraint("CK_LicencePendingReservation_ResolvedPair", "((\"LifecycleStatus\" = 'pending' AND \"ResolvedAtUtc\" IS NULL) OR (\"LifecycleStatus\" IN ('superseded', 'resolved') AND \"ResolvedAtUtc\" IS NOT NULL))");
                    table.ForeignKey(
                        name: "FK_LicencePendingReservation_LicenceSubject_LicenceSubjectId",
                        column: x => x.LicenceSubjectId,
                        principalSchema: "HushVoting",
                        principalTable: "LicenceSubject",
                        principalColumn: "LicenceSubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenceAssignment_OriginatingBlockIndex",
                schema: "HushVoting",
                table: "LicenceAssignment",
                column: "OriginatingBlockIndex");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceAssignment_OriginatingTransactionId",
                schema: "HushVoting",
                table: "LicenceAssignment",
                column: "OriginatingTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicenceAssignment_SupersededByAssignmentId",
                schema: "HushVoting",
                table: "LicenceAssignment",
                column: "SupersededByAssignmentId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LicenceAssignment_IndexOriginAllOrNone",
                schema: "HushVoting",
                table: "LicenceAssignment",
                sql: "((\"OriginatingTransactionId\" IS NULL AND \"OriginatingBlockIndex\" IS NULL AND \"OriginatingBlockTimeStampUtc\" IS NULL) OR (\"OriginatingTransactionId\" IS NOT NULL AND \"OriginatingBlockIndex\" IS NOT NULL AND \"OriginatingBlockTimeStampUtc\" IS NOT NULL))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LicenceAssignment_OriginatingBlockNonNegative",
                schema: "HushVoting",
                table: "LicenceAssignment",
                sql: "\"OriginatingBlockIndex\" IS NULL OR \"OriginatingBlockIndex\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LicenceAssignment_Source",
                schema: "HushVoting",
                table: "LicenceAssignment",
                sql: "\"Source\" IN ('default_free', 'migration_lazy_default', 'automatic_upgrade', 'automatic_expiry', 'baseline_free', 'confirmed_upgrade')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LicenceAssignment_SupersessionPair",
                schema: "HushVoting",
                table: "LicenceAssignment",
                sql: "((\"SupersededByAssignmentId\" IS NULL) OR (\"LifecycleStatus\" = 'superseded'))");

            migrationBuilder.CreateIndex(
                name: "IX_LicencePendingReservation_OriginatingTransactionId",
                schema: "HushVoting",
                table: "LicencePendingReservation",
                column: "OriginatingTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LicencePendingReservation_Subject",
                schema: "HushVoting",
                table: "LicencePendingReservation",
                column: "LicenceSubjectId",
                unique: true,
                filter: "\"LifecycleStatus\" = 'pending'");

            migrationBuilder.AddForeignKey(
                name: "FK_LicenceAssignment_LicenceAssignment_SupersededByAssignmentId",
                schema: "HushVoting",
                table: "LicenceAssignment",
                column: "SupersededByAssignmentId",
                principalSchema: "HushVoting",
                principalTable: "LicenceAssignment",
                principalColumn: "LicenceAssignmentId",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LicenceAssignment_LicenceAssignment_SupersededByAssignmentId",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropTable(
                name: "LicencePendingReservation",
                schema: "HushVoting");

            migrationBuilder.DropIndex(
                name: "IX_LicenceAssignment_OriginatingBlockIndex",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropIndex(
                name: "IX_LicenceAssignment_OriginatingTransactionId",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropIndex(
                name: "IX_LicenceAssignment_SupersededByAssignmentId",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LicenceAssignment_IndexOriginAllOrNone",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LicenceAssignment_OriginatingBlockNonNegative",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LicenceAssignment_Source",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropCheckConstraint(
                name: "CK_LicenceAssignment_SupersessionPair",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropColumn(
                name: "OriginatingBlockIndex",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropColumn(
                name: "OriginatingBlockTimeStampUtc",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropColumn(
                name: "OriginatingTransactionId",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.DropColumn(
                name: "SupersededByAssignmentId",
                schema: "HushVoting",
                table: "LicenceAssignment");

            migrationBuilder.AddCheckConstraint(
                name: "CK_LicenceAssignment_Source",
                schema: "HushVoting",
                table: "LicenceAssignment",
                sql: "\"Source\" IN ('default_free', 'migration_lazy_default', 'automatic_upgrade', 'automatic_expiry')");
        }
    }
}
