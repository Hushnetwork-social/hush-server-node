using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat112ProtocolPackageCatalogBinding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ApprovedProtocolPackageCatalogEntryRecord",
                schema: "Elections",
                columns: table => new
                {
                    PackageId = table.Column<string>(type: "varchar(160)", nullable: false),
                    ApprovalStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    IsLatestForCompatibleProfiles = table.Column<bool>(type: "boolean", nullable: false),
                    ExternalReviewStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    PackageVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    SpecPackageHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    ProofPackageHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    ReleaseManifestHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    CompatibleProfileIds = table.Column<string>(type: "jsonb", nullable: false),
                    SpecAccessLocations = table.Column<string>(type: "jsonb", nullable: false),
                    ProofAccessLocations = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApprovedProtocolPackageCatalogEntryRecord", x => new { x.PackageId, x.PackageVersion });
                });

            migrationBuilder.CreateTable(
                name: "ProtocolPackageBindingRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Source = table.Column<string>(type: "varchar(32)", nullable: false),
                    BoundAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SealedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExternalReviewStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    PackageId = table.Column<string>(type: "varchar(160)", nullable: false),
                    PackageVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    SelectedProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    SpecPackageHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    ProofPackageHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    ReleaseManifestHash = table.Column<string>(type: "varchar(64)", nullable: false),
                    PackageApprovalStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    SpecAccessLocations = table.Column<string>(type: "jsonb", nullable: false),
                    ProofAccessLocations = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    DraftRevision = table.Column<int>(type: "integer", nullable: false),
                    BoundByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProtocolPackageBindingRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedProtocolPackageCatalogEntryRecord_ApprovalStatus",
                schema: "Elections",
                table: "ApprovedProtocolPackageCatalogEntryRecord",
                column: "ApprovalStatus");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedProtocolPackageCatalogEntryRecord_IsLatestForCompat~",
                schema: "Elections",
                table: "ApprovedProtocolPackageCatalogEntryRecord",
                column: "IsLatestForCompatibleProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedProtocolPackageCatalogEntryRecord_PackageId",
                schema: "Elections",
                table: "ApprovedProtocolPackageCatalogEntryRecord",
                column: "PackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ApprovedProtocolPackageCatalogEntryRecord_PackageVersion",
                schema: "Elections",
                table: "ApprovedProtocolPackageCatalogEntryRecord",
                column: "PackageVersion");

            migrationBuilder.CreateIndex(
                name: "IX_ProtocolPackageBindingRecord_ElectionId_BoundAt",
                schema: "Elections",
                table: "ProtocolPackageBindingRecord",
                columns: new[] { "ElectionId", "BoundAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ProtocolPackageBindingRecord_ElectionId_DraftRevision",
                schema: "Elections",
                table: "ProtocolPackageBindingRecord",
                columns: new[] { "ElectionId", "DraftRevision" });

            migrationBuilder.CreateIndex(
                name: "IX_ProtocolPackageBindingRecord_ElectionId_Status",
                schema: "Elections",
                table: "ProtocolPackageBindingRecord",
                columns: new[] { "ElectionId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApprovedProtocolPackageCatalogEntryRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ProtocolPackageBindingRecord",
                schema: "Elections");
        }
    }
}
