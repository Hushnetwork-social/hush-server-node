using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat099BallotAcceptanceCheckoffNullifier : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionAcceptedBallotRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EncryptedBallotPackage = table.Column<string>(type: "text", nullable: false),
                    ProofBundle = table.Column<string>(type: "text", nullable: false),
                    BallotNullifier = table.Column<string>(type: "varchar(256)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAcceptedBallotRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCastIdempotencyRecord",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    IdempotencyKeyHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCastIdempotencyRecord", x => new { x.ElectionId, x.IdempotencyKeyHash });
                });

            migrationBuilder.CreateTable(
                name: "ElectionCheckoffConsumptionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ConsumedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    ParticipationStatus = table.Column<string>(type: "varchar(24)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCheckoffConsumptionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCommitmentRegistrationRecord",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LinkedActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    CommitmentHash = table.Column<string>(type: "varchar(256)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCommitmentRegistrationRecord", x => new { x.ElectionId, x.OrganizationVoterId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_AcceptedAt",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                columns: new[] { "ElectionId", "AcceptedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_BallotNullifier",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                columns: new[] { "ElectionId", "BallotNullifier" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCastIdempotencyRecord_ElectionId_RecordedAt",
                schema: "Elections",
                table: "ElectionCastIdempotencyRecord",
                columns: new[] { "ElectionId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCheckoffConsumptionRecord_ElectionId_ConsumedAt",
                schema: "Elections",
                table: "ElectionCheckoffConsumptionRecord",
                columns: new[] { "ElectionId", "ConsumedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCheckoffConsumptionRecord_ElectionId_OrganizationVo~",
                schema: "Elections",
                table: "ElectionCheckoffConsumptionRecord",
                columns: new[] { "ElectionId", "OrganizationVoterId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_CommitmentH~",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord",
                columns: new[] { "ElectionId", "CommitmentHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_LinkedActor~",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_RegisteredAt",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord",
                columns: new[] { "ElectionId", "RegisteredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionAcceptedBallotRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCastIdempotencyRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCheckoffConsumptionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCommitmentRegistrationRecord",
                schema: "Elections");
        }
    }
}
