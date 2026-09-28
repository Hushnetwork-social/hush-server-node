using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat018ElectionEntitlementEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionEntitlementCapture",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    LicenceSubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    OriginatingLicenceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PlanFamily = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpgradeRank = table.Column<int>(type: "integer", nullable: false),
                    EligibleVoterCap = table.Column<int>(type: "integer", nullable: true),
                    UnlimitedElectionPolicy = table.Column<bool>(type: "boolean", nullable: false),
                    TermKind = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    TermYears = table.Column<int>(type: "integer", nullable: false),
                    EffectiveFromUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AllowedGovernanceOptionIdsJson = table.Column<string>(type: "text", nullable: false),
                    AssignedCatalogueVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    AssignedCatalogueDigestSha256 = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EntitlementRevision = table.Column<long>(type: "bigint", nullable: false),
                    SelectedProfileId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    SelectedGovernanceOptionId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    FrozenEligibleVoterCount = table.Column<int>(type: "integer", nullable: false),
                    FrozenRosterBasisId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenBlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpenBlockHeight = table.Column<long>(type: "bigint", nullable: false),
                    OpenTransactionPosition = table.Column<int>(type: "integer", nullable: false),
                    OpenBlockTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GovernedProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionEntitlementCapture", x => x.ElectionId);
                    table.ForeignKey(
                        name: "FK_ElectionEntitlementCapture_ElectionRecord_ElectionId",
                        column: x => x.ElectionId,
                        principalSchema: "Elections",
                        principalTable: "ElectionRecord",
                        principalColumn: "ElectionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ElectionRosterLinkBoundary",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    LinkedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionRosterLinkBoundary", x => x.ElectionId);
                    table.ForeignKey(
                        name: "FK_ElectionRosterLinkBoundary_ElectionRecord_ElectionId",
                        column: x => x.ElectionId,
                        principalSchema: "Elections",
                        principalTable: "ElectionRecord",
                        principalColumn: "ElectionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEntitlementCapture_OpenTransactionId",
                schema: "Elections",
                table: "ElectionEntitlementCapture",
                column: "OpenTransactionId",
                unique: true);

            // Append-only evidence, including protection from direct SQL writes.
            migrationBuilder.Sql("""
                CREATE FUNCTION "Elections"."RejectEntitlementEvidenceMutation"() RETURNS trigger
                LANGUAGE plpgsql AS $$ BEGIN
                  RAISE EXCEPTION 'Election entitlement evidence is immutable' USING ERRCODE = '23514';
                END $$;
                CREATE TRIGGER "ImmutableCapture" BEFORE UPDATE OR DELETE ON "Elections"."ElectionEntitlementCapture"
                  FOR EACH ROW EXECUTE FUNCTION "Elections"."RejectEntitlementEvidenceMutation"();
                CREATE TRIGGER "ImmutableFirstLink" BEFORE UPDATE OR DELETE ON "Elections"."ElectionRosterLinkBoundary"
                  FOR EACH ROW EXECUTE FUNCTION "Elections"."RejectEntitlementEvidenceMutation"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // A downgrade must not discard captured completion rights or re-enable replacement.
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Elections"."ElectionEntitlementCapture")
                     OR EXISTS (SELECT 1 FROM "Elections"."ElectionRosterLinkBoundary") THEN
                    RAISE EXCEPTION 'Entitlement evidence exists; compatible forward fix required' USING ERRCODE = '23514';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ElectionEntitlementCapture",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionRosterLinkBoundary",
                schema: "Elections");
            migrationBuilder.Sql("DROP FUNCTION \"Elections\".\"RejectEntitlementEvidenceMutation\"()");
        }
    }
}
