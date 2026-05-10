using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat117PublicationProofTranscriptColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionPublicationProofSessionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    DeletionReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProofMode = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofConstruction = table.Column<string>(type: "varchar(128)", nullable: false),
                    StatementId = table.Column<string>(type: "varchar(128)", nullable: false),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    AcceptedBallotSetHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    PublishedBallotStreamHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    TranscriptHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ProofHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ServerVerifierOutputHash = table.Column<string>(type: "varchar(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationProofSessionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublicationProofTranscriptRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ProofSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    CiphertextSlotCount = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TranscriptVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofMode = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofConstruction = table.Column<string>(type: "varchar(128)", nullable: false),
                    StatementId = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    BallotDefinitionHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    BallotEncryptionSchemeVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    ElectionPublicKeyId = table.Column<string>(type: "varchar(128)", nullable: false),
                    AcceptedBallotSetHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    PublishedBallotStreamHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofSystemVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofBytes = table.Column<string>(type: "text", nullable: false),
                    ProofHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    TranscriptHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ExternalReviewStatus = table.Column<string>(type: "varchar(64)", nullable: false),
                    GeneratorReleaseHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    VerifierReleaseHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    StatementHashSha512 = table.Column<string>(type: "varchar(128)", nullable: true),
                    FiatShamirTranscriptHashSha512 = table.Column<string>(type: "varchar(128)", nullable: true),
                    CanonicalProofBytesHex = table.Column<string>(type: "text", nullable: true),
                    CanonicalProofHashSha512 = table.Column<string>(type: "varchar(128)", nullable: true),
                    CanonicalProofByteLength = table.Column<int>(type: "integer", nullable: true),
                    PublicPrivacyBoundary = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationProofTranscriptRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublicationWitnessDeletionReceiptRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ProofSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    WitnessCount = table.Column<int>(type: "integer", nullable: false),
                    DeletionStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WitnessSetHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    TranscriptHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    DeletionActorRef = table.Column<string>(type: "varchar(160)", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationWitnessDeletionReceiptRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublicationWitnessRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedBallotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedSequence = table.Column<long>(type: "bigint", nullable: true),
                    CustodyStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedEncryptedBallotHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    PublishedEncryptedBallotHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ProofMode = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofConstruction = table.Column<string>(type: "varchar(128)", nullable: false),
                    StatementId = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofProfileVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    SealedWitnessMaterial = table.Column<string>(type: "text", nullable: false),
                    SealedWitnessMaterialHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    SealAlgorithm = table.Column<string>(type: "varchar(96)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationWitnessRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofSessionRecord_ElectionId_StartedAt",
                schema: "Elections",
                table: "ElectionPublicationProofSessionRecord",
                columns: new[] { "ElectionId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofSessionRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionPublicationProofSessionRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofSessionRecord_ElectionId_WitnessSet~",
                schema: "Elections",
                table: "ElectionPublicationProofSessionRecord",
                columns: new[] { "ElectionId", "WitnessSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofTranscriptRecord_ElectionId_Generat~",
                schema: "Elections",
                table: "ElectionPublicationProofTranscriptRecord",
                columns: new[] { "ElectionId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofTranscriptRecord_ElectionId_Transcr~",
                schema: "Elections",
                table: "ElectionPublicationProofTranscriptRecord",
                columns: new[] { "ElectionId", "TranscriptHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofTranscriptRecord_ProofSessionId",
                schema: "Elections",
                table: "ElectionPublicationProofTranscriptRecord",
                column: "ProofSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessDeletionReceiptRecord_ElectionId_~",
                schema: "Elections",
                table: "ElectionPublicationWitnessDeletionReceiptRecord",
                columns: new[] { "ElectionId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessDeletionReceiptRecord_ProofSessio~",
                schema: "Elections",
                table: "ElectionPublicationWitnessDeletionReceiptRecord",
                column: "ProofSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessRecord_ElectionId_AcceptedBallotId",
                schema: "Elections",
                table: "ElectionPublicationWitnessRecord",
                columns: new[] { "ElectionId", "AcceptedBallotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessRecord_ElectionId_CustodyStatus",
                schema: "Elections",
                table: "ElectionPublicationWitnessRecord",
                columns: new[] { "ElectionId", "CustodyStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessRecord_ElectionId_WitnessSetId",
                schema: "Elections",
                table: "ElectionPublicationWitnessRecord",
                columns: new[] { "ElectionId", "WitnessSetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionPublicationProofSessionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationProofTranscriptRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationWitnessDeletionReceiptRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationWitnessRecord",
                schema: "Elections");
        }
    }
}
