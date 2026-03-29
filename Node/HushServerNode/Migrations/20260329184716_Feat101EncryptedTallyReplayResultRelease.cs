using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat101EncryptedTallyReplayResultRelease : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ClosedProgressStatus",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(40)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "OfficialResultArtifactId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OfficialResultVisibilityPolicy",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(40)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "UnofficialResultArtifactId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SessionPurpose",
                schema: "Elections",
                table: "ElectionFinalizationSessionRecord",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SessionPurpose",
                schema: "Elections",
                table: "ElectionFinalizationReleaseEvidenceRecord",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NodeEncryptedElectionPrivateKey",
                schema: "Elections",
                table: "ElectionEnvelopeAccessRecord",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "ElectionResultArtifactRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ArtifactKind = table.Column<string>(type: "varchar(24)", nullable: false),
                    Visibility = table.Column<string>(type: "varchar(32)", nullable: false),
                    NamedOptionResults = table.Column<string>(type: "jsonb", nullable: false),
                    BlankCount = table.Column<int>(type: "integer", nullable: false),
                    TotalVotedCount = table.Column<int>(type: "integer", nullable: false),
                    EligibleToVoteCount = table.Column<int>(type: "integer", nullable: false),
                    DidNotVoteCount = table.Column<int>(type: "integer", nullable: false),
                    DenominatorEvidence = table.Column<string>(type: "jsonb", nullable: false),
                    TallyReadyArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceResultArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "text", nullable: false),
                    RecordedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    EncryptedPayload = table.Column<string>(type: "text", nullable: true),
                    PublicPayload = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionResultArtifactRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionResultArtifactRecord_ElectionId_ArtifactKind",
                schema: "Elections",
                table: "ElectionResultArtifactRecord",
                columns: new[] { "ElectionId", "ArtifactKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionResultArtifactRecord_ElectionId_RecordedAt",
                schema: "Elections",
                table: "ElectionResultArtifactRecord",
                columns: new[] { "ElectionId", "RecordedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionResultArtifactRecord",
                schema: "Elections");

            migrationBuilder.DropColumn(
                name: "ClosedProgressStatus",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "OfficialResultArtifactId",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "OfficialResultVisibilityPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "UnofficialResultArtifactId",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "SessionPurpose",
                schema: "Elections",
                table: "ElectionFinalizationSessionRecord");

            migrationBuilder.DropColumn(
                name: "SessionPurpose",
                schema: "Elections",
                table: "ElectionFinalizationReleaseEvidenceRecord");

            migrationBuilder.DropColumn(
                name: "NodeEncryptedElectionPrivateKey",
                schema: "Elections",
                table: "ElectionEnvelopeAccessRecord");
        }
    }
}
