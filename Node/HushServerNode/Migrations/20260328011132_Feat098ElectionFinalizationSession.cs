using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat098ElectionFinalizationSession : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionFinalizationReleaseEvidenceRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizationSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ReleaseMode = table.Column<string>(type: "varchar(32)", nullable: false),
                    CloseArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedBallotSetHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    FinalEncryptedTallyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    TargetTallyId = table.Column<string>(type: "varchar(256)", nullable: false),
                    AcceptedShareCount = table.Column<int>(type: "integer", nullable: false),
                    AcceptedTrustees = table.Column<string>(type: "jsonb", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionFinalizationReleaseEvidenceRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionFinalizationSessionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    GovernedProposalId = table.Column<Guid>(type: "uuid", nullable: true),
                    GovernanceMode = table.Column<string>(type: "varchar(32)", nullable: false),
                    CloseArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedBallotSetHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    FinalEncryptedTallyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    TargetTallyId = table.Column<string>(type: "varchar(256)", nullable: false),
                    CeremonySnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    RequiredShareCount = table.Column<int>(type: "integer", nullable: false),
                    EligibleTrustees = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CreatedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReleaseEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    LatestBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionFinalizationSessionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionFinalizationShareRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizationSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    TrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    TrusteeDisplayName = table.Column<string>(type: "varchar(200)", nullable: true),
                    SubmittedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ShareIndex = table.Column<int>(type: "integer", nullable: false),
                    ShareVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    TargetType = table.Column<string>(type: "varchar(32)", nullable: false),
                    ClaimedCloseArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClaimedAcceptedBallotSetHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ClaimedFinalEncryptedTallyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ClaimedTargetTallyId = table.Column<string>(type: "varchar(256)", nullable: false),
                    ClaimedCeremonyVersionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClaimedTallyPublicKeyFingerprint = table.Column<string>(type: "varchar(256)", nullable: true),
                    ShareMaterial = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionFinalizationShareRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionFinalizationReleaseEvidenceRecord_ElectionId",
                schema: "Elections",
                table: "ElectionFinalizationReleaseEvidenceRecord",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionFinalizationReleaseEvidenceRecord_FinalizationSessi~",
                schema: "Elections",
                table: "ElectionFinalizationReleaseEvidenceRecord",
                column: "FinalizationSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionFinalizationSessionRecord_ElectionId_CloseArtifactId",
                schema: "Elections",
                table: "ElectionFinalizationSessionRecord",
                columns: new[] { "ElectionId", "CloseArtifactId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionFinalizationSessionRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionFinalizationSessionRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionFinalizationShareRecord_FinalizationSessionId_Submi~",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord",
                columns: new[] { "FinalizationSessionId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionFinalizationShareRecord_FinalizationSessionId_Trust~",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord",
                columns: new[] { "FinalizationSessionId", "TrusteeUserAddress", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionFinalizationReleaseEvidenceRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionFinalizationSessionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionFinalizationShareRecord",
                schema: "Elections");
        }
    }
}
