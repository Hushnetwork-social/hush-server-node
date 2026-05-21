using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushNode.Elections.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Feat138VoidDecisionContracts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "UnofficialResultArtifactId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AlterColumn<Guid>(
                name: "TallyReadyArtifactId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "PackageKind",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "varchar(32)",
                nullable: false,
                defaultValue: "FinalResult");

            migrationBuilder.AddColumn<DateTime>(
                name: "SupersededAt",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SupersededByVoidDecisionId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidDecisionId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VoidPublicationAttemptId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ElectionVoidDecisionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    VoidBoundaryArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    CurrentPublicationAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicationStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ActorRole = table.Column<string>(type: "varchar(32)", nullable: false),
                    PreviousLifecycleState = table.Column<string>(type: "varchar(32)", nullable: false),
                    ResultingLifecycleState = table.Column<string>(type: "varchar(32)", nullable: false),
                    PublicJustification = table.Column<string>(type: "varchar(1000)", nullable: false),
                    PublicJustificationHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    EvidenceReferences = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionVoidDecisionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionVoidPublicationAttemptRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    VoidDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    PreviousAttemptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ArtifactCount = table.Column<int>(type: "integer", nullable: false),
                    AttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SealedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FrozenEvidenceHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    FrozenEvidenceFingerprint = table.Column<string>(type: "varchar(256)", nullable: false),
                    PackageHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    PublicStatusArtifactRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    VoidPackageArtifactRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    AttemptedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionVoidPublicationAttemptRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionVoidSupersededArtifactRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    VoidDecisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ArtifactKind = table.Column<string>(type: "varchar(32)", nullable: false),
                    ReportPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ArtifactRef = table.Column<string>(type: "varchar(512)", nullable: false),
                    ArtifactHash = table.Column<string>(type: "varchar(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionVoidSupersededArtifactRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportPackageRecord_ElectionId_PackageKind",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                columns: new[] { "ElectionId", "PackageKind" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionReportPackageRecord_VoidDecisionId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                column: "VoidDecisionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidDecisionRecord_ElectionId",
                schema: "Elections",
                table: "ElectionVoidDecisionRecord",
                column: "ElectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidDecisionRecord_ElectionId_DecidedAt",
                schema: "Elections",
                table: "ElectionVoidDecisionRecord",
                columns: new[] { "ElectionId", "DecidedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidDecisionRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionVoidDecisionRecord",
                column: "SourceTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidDecisionRecord_VoidBoundaryArtifactId",
                schema: "Elections",
                table: "ElectionVoidDecisionRecord",
                column: "VoidBoundaryArtifactId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidPublicationAttemptRecord_ElectionId_AttemptNumb~",
                schema: "Elections",
                table: "ElectionVoidPublicationAttemptRecord",
                columns: new[] { "ElectionId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidPublicationAttemptRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionVoidPublicationAttemptRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidPublicationAttemptRecord_ReportPackageId",
                schema: "Elections",
                table: "ElectionVoidPublicationAttemptRecord",
                column: "ReportPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidPublicationAttemptRecord_VoidDecisionId_Attempt~",
                schema: "Elections",
                table: "ElectionVoidPublicationAttemptRecord",
                columns: new[] { "VoidDecisionId", "AttemptNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidSupersededArtifactRecord_ElectionId",
                schema: "Elections",
                table: "ElectionVoidSupersededArtifactRecord",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidSupersededArtifactRecord_ReportArtifactId",
                schema: "Elections",
                table: "ElectionVoidSupersededArtifactRecord",
                column: "ReportArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidSupersededArtifactRecord_ReportPackageId",
                schema: "Elections",
                table: "ElectionVoidSupersededArtifactRecord",
                column: "ReportPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoidSupersededArtifactRecord_VoidDecisionId_Artifac~",
                schema: "Elections",
                table: "ElectionVoidSupersededArtifactRecord",
                columns: new[] { "VoidDecisionId", "ArtifactKind" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionVoidDecisionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionVoidPublicationAttemptRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionVoidSupersededArtifactRecord",
                schema: "Elections");

            migrationBuilder.DropIndex(
                name: "IX_ElectionReportPackageRecord_ElectionId_PackageKind",
                schema: "Elections",
                table: "ElectionReportPackageRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionReportPackageRecord_VoidDecisionId",
                schema: "Elections",
                table: "ElectionReportPackageRecord");

            migrationBuilder.DropColumn(
                name: "PackageKind",
                schema: "Elections",
                table: "ElectionReportPackageRecord");

            migrationBuilder.DropColumn(
                name: "SupersededAt",
                schema: "Elections",
                table: "ElectionReportPackageRecord");

            migrationBuilder.DropColumn(
                name: "SupersededByVoidDecisionId",
                schema: "Elections",
                table: "ElectionReportPackageRecord");

            migrationBuilder.DropColumn(
                name: "VoidDecisionId",
                schema: "Elections",
                table: "ElectionReportPackageRecord");

            migrationBuilder.DropColumn(
                name: "VoidPublicationAttemptId",
                schema: "Elections",
                table: "ElectionReportPackageRecord");

            migrationBuilder.AlterColumn<Guid>(
                name: "UnofficialResultArtifactId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "TallyReadyArtifactId",
                schema: "Elections",
                table: "ElectionReportPackageRecord",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
