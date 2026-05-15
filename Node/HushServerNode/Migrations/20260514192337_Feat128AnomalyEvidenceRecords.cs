using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat128AnomalyEvidenceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionAnomalyAttachmentManifestRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AttachmentKindId = table.Column<string>(type: "varchar(96)", nullable: false),
                    EncryptedPayloadReference = table.Column<string>(type: "varchar(160)", nullable: false),
                    EncryptedPayloadHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContentKeyWrapsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "varchar(128)", nullable: false),
                    ValidationStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    ScannerStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    PayloadAvailabilityStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    ClarificationRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ActorRoleId = table.Column<string>(type: "varchar(64)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyAttachmentManifestRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyEvidenceRedactionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    TargetKindId = table.Column<string>(type: "varchar(96)", nullable: false),
                    TargetId = table.Column<string>(type: "varchar(160)", nullable: false),
                    ReasonCodeId = table.Column<string>(type: "varchar(96)", nullable: false),
                    OriginalHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ReplacementManifestHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    TombstoneStatusId = table.Column<string>(type: "varchar(64)", nullable: true),
                    HoldReference = table.Column<string>(type: "text", nullable: true),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyEvidenceRedactionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyRestrictedPayloadRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadReference = table.Column<string>(type: "varchar(160)", nullable: false),
                    EncryptedPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedPayloadHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "varchar(128)", nullable: false),
                    ScannerStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    PayloadAvailabilityStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyRestrictedPayloadRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_AnomalyThreadId_Att~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "AnomalyThreadId", "AttachmentKindId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_AnomalyThreadId_Cla~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "AnomalyThreadId", "ClarificationRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_ElectionId_PayloadA~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "ElectionId", "PayloadAvailabilityStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_ElectionId_ScannerS~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "ElectionId", "ScannerStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_EncryptedPayloadRef~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                column: "EncryptedPayloadReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_EventId",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                column: "SourceTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_AnomalyThreadId_Targ~",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
                columns: new[] { "AnomalyThreadId", "TargetKindId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_ElectionId_ReasonCod~",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
                columns: new[] { "ElectionId", "ReasonCodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_EventId",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
                column: "SourceTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRestrictedPayloadRecord_AnomalyThreadId_Payl~",
                schema: "Elections",
                table: "ElectionAnomalyRestrictedPayloadRecord",
                columns: new[] { "AnomalyThreadId", "PayloadAvailabilityStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRestrictedPayloadRecord_ElectionId_ScannerSt~",
                schema: "Elections",
                table: "ElectionAnomalyRestrictedPayloadRecord",
                columns: new[] { "ElectionId", "ScannerStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRestrictedPayloadRecord_PayloadReference",
                schema: "Elections",
                table: "ElectionAnomalyRestrictedPayloadRecord",
                column: "PayloadReference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionAnomalyAttachmentManifestRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyEvidenceRedactionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyRestrictedPayloadRecord",
                schema: "Elections");
        }
    }
}
