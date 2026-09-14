using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat014LicenceCacheOutbox : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LicenceCacheOutbox",
                schema: "HushVoting",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    LicenceSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    CommittedRevision = table.Column<long>(type: "bigint", nullable: false),
                    ChangeKind = table.Column<string>(type: "varchar(40)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AvailableAfterUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    AttemptCount = table.Column<int>(type: "integer", nullable: false),
                    LeaseOwnerToken = table.Column<string>(type: "varchar(64)", nullable: true),
                    LeaseExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DeliveredUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastSafeErrorCode = table.Column<string>(type: "varchar(64)", nullable: true),
                    LastAttemptUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LicenceCacheOutbox", x => x.Id);
                    table.CheckConstraint("CK_LicenceCacheOutbox_AttemptNonNegative", "\"AttemptCount\" >= 0");
                    table.CheckConstraint("CK_LicenceCacheOutbox_AvailableAfterCreated", "\"AvailableAfterUtc\" >= \"CreatedUtc\"");
                    table.CheckConstraint("CK_LicenceCacheOutbox_ChangeKind", "\"ChangeKind\" IN ('provisioned_default', 'provisioned_migration_default', 'activated_higher_plan', 'expired_to_default')");
                    table.CheckConstraint("CK_LicenceCacheOutbox_ErrorCodeBounded", "\"LastSafeErrorCode\" IS NULL OR char_length(\"LastSafeErrorCode\") BETWEEN 1 AND 64");
                    table.CheckConstraint("CK_LicenceCacheOutbox_LeaseConsistent", "(\"LeaseOwnerToken\" IS NULL AND \"LeaseExpiresUtc\" IS NULL) OR (\"LeaseOwnerToken\" IS NOT NULL AND \"LeaseExpiresUtc\" IS NOT NULL)");
                    table.CheckConstraint("CK_LicenceCacheOutbox_RevisionNonNegative", "\"CommittedRevision\" >= 0");
                    table.ForeignKey(
                        name: "FK_LicenceCacheOutbox_LicenceSubject_LicenceSubjectId",
                        column: x => x.LicenceSubjectId,
                        principalSchema: "HushVoting",
                        principalTable: "LicenceSubject",
                        principalColumn: "LicenceSubjectId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LicenceCacheOutbox_DeliveredCleanup",
                schema: "HushVoting",
                table: "LicenceCacheOutbox",
                column: "DeliveredUtc",
                filter: "\"DeliveredUtc\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceCacheOutbox_PendingClaimOrder",
                schema: "HushVoting",
                table: "LicenceCacheOutbox",
                columns: new[] { "DeliveredUtc", "AvailableAfterUtc", "CreatedUtc", "Id" },
                filter: "\"DeliveredUtc\" IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LicenceCacheOutbox_Subject",
                schema: "HushVoting",
                table: "LicenceCacheOutbox",
                column: "LicenceSubjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Destructive rollback guard (FEAT-014 outbox contract): undelivered cache-delivery rows
            // are never discarded; rollback is refused while any pending row exists. Production
            // recovery uses a forward-fix migration.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "HushVoting"."LicenceCacheOutbox" WHERE "DeliveredUtc" IS NULL) THEN
                        RAISE EXCEPTION 'Destructive rollback refused: HushVoting.LicenceCacheOutbox has undelivered rows; use a forward-fix migration.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.DropTable(
                name: "LicenceCacheOutbox",
                schema: "HushVoting");
        }
    }
}
