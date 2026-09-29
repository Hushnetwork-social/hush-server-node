using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat018RejectedOpenOutcome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionOpenRejection",
                schema: "Elections",
                columns: table => new
                {
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    BlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockHeight = table.Column<long>(type: "bigint", nullable: false),
                    TransactionPosition = table.Column<int>(type: "integer", nullable: false),
                    BlockTimeUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    GovernedProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    ErrorCategory = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<int>(type: "integer", nullable: false),
                    SchemaVersion = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionOpenRejection", x => x.TransactionId);
                    table.ForeignKey(
                        name: "FK_ElectionOpenRejection_ElectionRecord_ElectionId",
                        column: x => x.ElectionId,
                        principalSchema: "Elections",
                        principalTable: "ElectionRecord",
                        principalColumn: "ElectionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionOpenRejection_ElectionId",
                schema: "Elections",
                table: "ElectionOpenRejection",
                column: "ElectionId");
            migrationBuilder.Sql("""
                CREATE TRIGGER "ImmutableOpenRejection" BEFORE UPDATE OR DELETE ON "Elections"."ElectionOpenRejection"
                  FOR EACH ROW EXECUTE FUNCTION "Elections"."RejectEntitlementEvidenceMutation"();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                  IF EXISTS (SELECT 1 FROM "Elections"."ElectionOpenRejection")
                     OR EXISTS (SELECT 1 FROM "Elections"."ElectionEntitlementCapture") THEN
                    RAISE EXCEPTION 'Open outcome history exists; compatible forward fix required' USING ERRCODE = '23514';
                  END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ElectionOpenRejection",
                schema: "Elections");
        }
    }
}
