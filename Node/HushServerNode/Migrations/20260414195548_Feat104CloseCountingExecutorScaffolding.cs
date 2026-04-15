using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat104CloseCountingExecutorScaffolding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionCloseCountingJobRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizationSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    CloseArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedBallotSetHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    FinalEncryptedTallyHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    TargetTallyId = table.Column<string>(type: "varchar(256)", nullable: false),
                    CeremonyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TallyPublicKeyFingerprint = table.Column<string>(type: "varchar(256)", nullable: false),
                    RequiredShareCount = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ThresholdReachedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    LatestTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    LatestBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCloseCountingJobRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionExecutorSessionKeyEnvelopeRecord",
                schema: "Elections",
                columns: table => new
                {
                    CloseCountingJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExecutorSessionPublicKey = table.Column<string>(type: "text", nullable: false),
                    SealedExecutorSessionPrivateKey = table.Column<string>(type: "text", nullable: false),
                    KeyAlgorithm = table.Column<string>(type: "varchar(96)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DestroyedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SealedByServiceIdentity = table.Column<string>(type: "varchar(160)", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionExecutorSessionKeyEnvelopeRecord", x => x.CloseCountingJobId);
                });

            migrationBuilder.CreateTable(
                name: "ElectionTallyExecutorLeaseRecord",
                schema: "Elections",
                columns: table => new
                {
                    CloseCountingJobId = table.Column<Guid>(type: "uuid", nullable: false),
                    LeaseHolderId = table.Column<string>(type: "varchar(160)", nullable: false),
                    LeaseEpoch = table.Column<long>(type: "bigint", nullable: false),
                    LeasedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LeaseExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastHeartbeatAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ReleaseReason = table.Column<string>(type: "text", nullable: true),
                    CompletionReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionTallyExecutorLeaseRecord", x => x.CloseCountingJobId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCloseCountingJobRecord_ElectionId_CreatedAt",
                schema: "Elections",
                table: "ElectionCloseCountingJobRecord",
                columns: new[] { "ElectionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCloseCountingJobRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionCloseCountingJobRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCloseCountingJobRecord_FinalizationSessionId",
                schema: "Elections",
                table: "ElectionCloseCountingJobRecord",
                column: "FinalizationSessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionCloseCountingJobRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionExecutorSessionKeyEnvelopeRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionTallyExecutorLeaseRecord",
                schema: "Elections");
        }
    }
}
