using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat097ElectionCeremonyLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CeremonySnapshot",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "jsonb",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ElectionCeremonyMessageEnvelopeRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    CeremonyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    SenderTrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    RecipientTrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: true),
                    MessageType = table.Column<string>(type: "varchar(96)", nullable: false),
                    PayloadVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    EncryptedPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "varchar(256)", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCeremonyMessageEnvelopeRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCeremonyProfileRecord",
                schema: "Elections",
                columns: table => new
                {
                    ProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(200)", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    ProviderKey = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProfileVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    TrusteeCount = table.Column<int>(type: "integer", nullable: false),
                    RequiredApprovalCount = table.Column<int>(type: "integer", nullable: false),
                    DevOnly = table.Column<bool>(type: "boolean", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCeremonyProfileRecord", x => x.ProfileId);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCeremonyShareCustodyRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    CeremonyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ShareVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    PasswordProtected = table.Column<bool>(type: "boolean", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    LastExportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastImportFailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastImportFailureReason = table.Column<string>(type: "text", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCeremonyShareCustodyRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCeremonyTranscriptEventRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    CeremonyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    EventType = table.Column<string>(type: "varchar(48)", nullable: false),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: true),
                    TrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: true),
                    TrusteeDisplayName = table.Column<string>(type: "varchar(200)", nullable: true),
                    TrusteeState = table.Column<string>(type: "varchar(40)", nullable: true),
                    EventSummary = table.Column<string>(type: "text", nullable: false),
                    EvidenceReference = table.Column<string>(type: "text", nullable: true),
                    RestartReason = table.Column<string>(type: "text", nullable: true),
                    TallyPublicKeyFingerprint = table.Column<string>(type: "varchar(256)", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCeremonyTranscriptEventRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCeremonyTrusteeStateRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    CeremonyVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    TrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    TrusteeDisplayName = table.Column<string>(type: "varchar(200)", nullable: true),
                    State = table.Column<string>(type: "varchar(40)", nullable: false),
                    TransportPublicKeyFingerprint = table.Column<string>(type: "varchar(256)", nullable: true),
                    TransportPublicKeyPublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SelfTestSucceededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    MaterialSubmittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidationFailedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ValidationFailureReason = table.Column<string>(type: "text", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ShareVersion = table.Column<string>(type: "varchar(64)", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCeremonyTrusteeStateRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionCeremonyVersionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    ProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    Status = table.Column<string>(type: "varchar(24)", nullable: false),
                    TrusteeCount = table.Column<int>(type: "integer", nullable: false),
                    RequiredApprovalCount = table.Column<int>(type: "integer", nullable: false),
                    BoundTrustees = table.Column<string>(type: "jsonb", nullable: false),
                    StartedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SupersededReason = table.Column<string>(type: "text", nullable: true),
                    TallyPublicKeyFingerprint = table.Column<string>(type: "varchar(256)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCeremonyVersionRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyMessageEnvelopeRecord_CeremonyVersionId_Rec~",
                schema: "Elections",
                table: "ElectionCeremonyMessageEnvelopeRecord",
                columns: new[] { "CeremonyVersionId", "RecipientTrusteeUserAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyMessageEnvelopeRecord_CeremonyVersionId_Sub~",
                schema: "Elections",
                table: "ElectionCeremonyMessageEnvelopeRecord",
                columns: new[] { "CeremonyVersionId", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyProfileRecord_DevOnly",
                schema: "Elections",
                table: "ElectionCeremonyProfileRecord",
                column: "DevOnly");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyProfileRecord_TrusteeCount_RequiredApproval~",
                schema: "Elections",
                table: "ElectionCeremonyProfileRecord",
                columns: new[] { "TrusteeCount", "RequiredApprovalCount" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyShareCustodyRecord_CeremonyVersionId_Truste~",
                schema: "Elections",
                table: "ElectionCeremonyShareCustodyRecord",
                columns: new[] { "CeremonyVersionId", "TrusteeUserAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyShareCustodyRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionCeremonyShareCustodyRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyTranscriptEventRecord_CeremonyVersionId_Occ~",
                schema: "Elections",
                table: "ElectionCeremonyTranscriptEventRecord",
                columns: new[] { "CeremonyVersionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyTranscriptEventRecord_ElectionId_VersionNum~",
                schema: "Elections",
                table: "ElectionCeremonyTranscriptEventRecord",
                columns: new[] { "ElectionId", "VersionNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyTrusteeStateRecord_CeremonyVersionId_Truste~",
                schema: "Elections",
                table: "ElectionCeremonyTrusteeStateRecord",
                columns: new[] { "CeremonyVersionId", "TrusteeUserAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyTrusteeStateRecord_ElectionId_State",
                schema: "Elections",
                table: "ElectionCeremonyTrusteeStateRecord",
                columns: new[] { "ElectionId", "State" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyVersionRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionCeremonyVersionRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCeremonyVersionRecord_ElectionId_VersionNumber",
                schema: "Elections",
                table: "ElectionCeremonyVersionRecord",
                columns: new[] { "ElectionId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionCeremonyMessageEnvelopeRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCeremonyProfileRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCeremonyShareCustodyRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCeremonyTranscriptEventRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCeremonyTrusteeStateRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionCeremonyVersionRecord",
                schema: "Elections");

            migrationBuilder.DropColumn(
                name: "CeremonySnapshot",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");
        }
    }
}
