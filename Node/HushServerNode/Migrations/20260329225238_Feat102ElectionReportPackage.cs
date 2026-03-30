using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat102ElectionReportPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionReportAccessGrantRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    GrantRole = table.Column<string>(type: "varchar(32)", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    GrantedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionReportAccessGrantRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionReportArtifactRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReportPackageId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ArtifactKind = table.Column<string>(type: "varchar(64)", nullable: false),
                    Format = table.Column<string>(type: "varchar(16)", nullable: false),
                    AccessScope = table.Column<string>(type: "varchar(40)", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    PairedArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    FileName = table.Column<string>(type: "varchar(256)", nullable: false),
                    MediaType = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContentHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionReportArtifactRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionReportPackageRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    PreviousAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalizationSessionId = table.Column<Guid>(type: "uuid", nullable: true),
                    TallyReadyArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnofficialResultArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficialResultArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalizeArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseBoundaryArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseEligibilitySnapshotId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalizationReleaseEvidenceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    ArtifactCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SealedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FrozenEvidenceHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    FrozenEvidenceFingerprint = table.Column<string>(type: "varchar(256)", nullable: false),
                    PackageHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    AttemptedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionReportPackageRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportAccessGrantRecord_ActorPublicAddress",
                schema: "Elections",
                table: "ElectionReportAccessGrantRecord",
                column: "ActorPublicAddress");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportAccessGrantRecord_ElectionId_ActorPublicAddre~",
                schema: "Elections",
                table: "ElectionReportAccessGrantRecord",
                columns: new[] { "ElectionId", "ActorPublicAddress", "GrantRole" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportArtifactRecord_ElectionId",
                schema: "Elections",
                table: "ElectionReportArtifactRecord",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportArtifactRecord_ReportPackageId_ArtifactKind",
                schema: "Elections",
                table: "ElectionReportArtifactRecord",
                columns: new[] { "ReportPackageId", "ArtifactKind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportArtifactRecord_ReportPackageId_SortOrder",
                schema: "Elections",
                table: "ElectionReportArtifactRecord",
                columns: new[] { "ReportPackageId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportPackageRecord_ElectionId_AttemptedAt",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                columns: new[] { "ElectionId", "AttemptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportPackageRecord_ElectionId_AttemptNumber",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                columns: new[] { "ElectionId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportPackageRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                columns: new[] { "ElectionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionReportAccessGrantRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionReportArtifactRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionReportPackageRecord",
                schema: "Elections");
        }
    }
}
