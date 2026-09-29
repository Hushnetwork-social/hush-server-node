using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat018IndexCompletion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionIndexCheckpoint",
                schema: "Elections",
                columns: table => new
                {
                    BlockHeight = table.Column<long>(type: "bigint", nullable: false),
                    BlockId = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockHash = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HistoryDigestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    PolicyVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionIndexCheckpoint", x => x.BlockHeight);
                });

            migrationBuilder.Sql("""
                CREATE TRIGGER "ImmutableIndexCheckpoint" BEFORE UPDATE OR DELETE ON "Elections"."ElectionIndexCheckpoint"
                    FOR EACH ROW EXECUTE FUNCTION "Elections"."RejectEntitlementEvidenceMutation"();
                """);
            migrationBuilder.CreateIndex(
                name: "IX_ElectionIndexCheckpoint_BlockId",
                schema: "Elections",
                table: "ElectionIndexCheckpoint",
                column: "BlockId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$ BEGIN
                    IF EXISTS (SELECT 1 FROM "Elections"."ElectionIndexCheckpoint") THEN
                        RAISE EXCEPTION 'election_checkpoint_rollback_refused';
                    END IF;
                END $$;
                """);
            migrationBuilder.DropTable(
                name: "ElectionIndexCheckpoint",
                schema: "Elections");
        }
    }
}
