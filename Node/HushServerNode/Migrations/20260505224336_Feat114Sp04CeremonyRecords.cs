using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat114Sp04CeremonyRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparedBallotHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "varchar(256)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "varchar(256)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptCommitmentScheme",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "varchar(160)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ElectionPreparedBallotCommitmentRecord",
                schema: "Elections",
                columns: table => new
                {
                    PreparedBallotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    BallotDefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "varchar(24)", nullable: false),
                    PrecommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SpoilMarkerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedBallotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpoiledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CastAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    LinkedActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    PreparedBallotHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    BallotDefinitionHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CeremonyProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofStatementId = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPreparedBallotCommitmentRecord", x => x.PreparedBallotId);
                });

            migrationBuilder.CreateTable(
                name: "ElectionSpoiledPreparedBallotRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    PreparedBallotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpoiledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparedBallotHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    SpoiledTranscriptHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    SpoilRecordHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    LocalVerifierVersion = table.Column<string>(type: "varchar(96)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionSpoiledPreparedBallotRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionVoterCeremonyRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    BallotDefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    PreparedPackageCount = table.Column<int>(type: "integer", nullable: false),
                    SpoiledPackageCount = table.Column<int>(type: "integer", nullable: false),
                    FinalState = table.Column<string>(type: "varchar(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalAcceptedBallotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    LinkedActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    CeremonyProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    BallotDefinitionHash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionVoterCeremonyRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                columns: new[] { "ElectionId", "PreparedBallotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                columns: new[] { "ElectionId", "ReceiptCommitment" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_Organizat~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "OrganizationVoterId", "PrecommittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_PreparedB~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "PreparedBallotHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_State",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionSpoiledPreparedBallotRecord_ElectionId_PreparedBall~",
                schema: "Elections",
                table: "ElectionSpoiledPreparedBallotRecord",
                columns: new[] { "ElectionId", "PreparedBallotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionSpoiledPreparedBallotRecord_ElectionId_SpoiledAt",
                schema: "Elections",
                table: "ElectionSpoiledPreparedBallotRecord",
                columns: new[] { "ElectionId", "SpoiledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionSpoiledPreparedBallotRecord_ElectionId_SpoilRecordH~",
                schema: "Elections",
                table: "ElectionSpoiledPreparedBallotRecord",
                columns: new[] { "ElectionId", "SpoilRecordHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_FinalState",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "FinalState" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_LinkedActorPublicAdd~",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_OrganizationVoterId",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "OrganizationVoterId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionPreparedBallotCommitmentRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionSpoiledPreparedBallotRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionVoterCeremonyRecord",
                schema: "Elections");

            migrationBuilder.DropIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "PreparedBallotHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "ReceiptCommitmentScheme",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");
        }
    }
}
