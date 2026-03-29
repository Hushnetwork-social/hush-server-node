using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat100BallotMemPoolPublicationRerandomization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "TallyReadyArtifactId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AcceptedBallotCount",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "PublishedBallotCount",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "PublishedBallotStreamHash",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ElectionBallotMemPoolRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AcceptedBallotId = table.Column<Guid>(type: "uuid", nullable: false),
                    QueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionBallotMemPoolRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublicationIssueRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    IssueCode = table.Column<string>(type: "varchar(64)", nullable: false),
                    OccurrenceCount = table.Column<int>(type: "integer", nullable: false),
                    FirstObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastObservedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LatestBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    LatestBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationIssueRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublishedBallotRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    PublicationSequence = table.Column<long>(type: "bigint", nullable: false),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    EncryptedBallotPackage = table.Column<string>(type: "text", nullable: false),
                    ProofBundle = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublishedBallotRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionBallotMemPoolRecord_ElectionId_AcceptedBallotId",
                schema: "Elections",
                table: "ElectionBallotMemPoolRecord",
                columns: new[] { "ElectionId", "AcceptedBallotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionBallotMemPoolRecord_ElectionId_QueuedAt",
                schema: "Elections",
                table: "ElectionBallotMemPoolRecord",
                columns: new[] { "ElectionId", "QueuedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationIssueRecord_ElectionId_IssueCode",
                schema: "Elections",
                table: "ElectionPublicationIssueRecord",
                columns: new[] { "ElectionId", "IssueCode" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublishedBallotRecord_ElectionId_PublicationSequence",
                schema: "Elections",
                table: "ElectionPublishedBallotRecord",
                columns: new[] { "ElectionId", "PublicationSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublishedBallotRecord_ElectionId_PublishedAt",
                schema: "Elections",
                table: "ElectionPublishedBallotRecord",
                columns: new[] { "ElectionId", "PublishedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionBallotMemPoolRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationIssueRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublishedBallotRecord",
                schema: "Elections");

            migrationBuilder.DropColumn(
                name: "TallyReadyArtifactId",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "AcceptedBallotCount",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "PublishedBallotCount",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "PublishedBallotStreamHash",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");
        }
    }
}
