using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat094ElectionDomainModelLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Elections");

            migrationBuilder.CreateTable(
                name: "ElectionBoundaryArtifactRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ArtifactType = table.Column<string>(type: "varchar(24)", nullable: false),
                    LifecycleState = table.Column<string>(type: "varchar(24)", nullable: false),
                    SourceDraftRevision = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
                    Policy = table.Column<string>(type: "jsonb", nullable: false),
                    Options = table.Column<string>(type: "jsonb", nullable: false),
                    AcknowledgedWarningCodes = table.Column<string>(type: "jsonb", nullable: false),
                    TrusteeSnapshot = table.Column<string>(type: "jsonb", nullable: true),
                    FrozenEligibleVoterSetHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    TrusteePolicyExecutionReference = table.Column<string>(type: "text", nullable: true),
                    ReportingPolicyExecutionReference = table.Column<string>(type: "text", nullable: true),
                    ReviewWindowExecutionReference = table.Column<string>(type: "text", nullable: true),
                    AcceptedBallotSetHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    FinalEncryptedTallyHash = table.Column<byte[]>(type: "bytea", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionBoundaryArtifactRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionDraftSnapshotRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    DraftRevision = table.Column<int>(type: "integer", nullable: false),
                    Metadata = table.Column<string>(type: "jsonb", nullable: false),
                    Policy = table.Column<string>(type: "jsonb", nullable: false),
                    Options = table.Column<string>(type: "jsonb", nullable: false),
                    AcknowledgedWarningCodes = table.Column<string>(type: "jsonb", nullable: false),
                    SnapshotReason = table.Column<string>(type: "text", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionDraftSnapshotRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionRecord",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    ShortDescription = table.Column<string>(type: "text", nullable: true),
                    OwnerPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ExternalReferenceCode = table.Column<string>(type: "varchar(256)", nullable: true),
                    LifecycleState = table.Column<string>(type: "varchar(32)", nullable: false),
                    ElectionClass = table.Column<string>(type: "varchar(64)", nullable: false),
                    BindingStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    GovernanceMode = table.Column<string>(type: "varchar(32)", nullable: false),
                    DisclosureMode = table.Column<string>(type: "varchar(64)", nullable: false),
                    ParticipationPrivacyMode = table.Column<string>(type: "varchar(96)", nullable: false),
                    VoteUpdatePolicy = table.Column<string>(type: "varchar(64)", nullable: false),
                    EligibilitySourceType = table.Column<string>(type: "varchar(64)", nullable: false),
                    EligibilityMutationPolicy = table.Column<string>(type: "varchar(96)", nullable: false),
                    OutcomeRule = table.Column<string>(type: "jsonb", nullable: false),
                    ApprovedClientApplications = table.Column<string>(type: "jsonb", nullable: false),
                    ProtocolOmegaVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    ReportingPolicy = table.Column<string>(type: "varchar(64)", nullable: false),
                    ReviewWindowPolicy = table.Column<string>(type: "varchar(64)", nullable: false),
                    CurrentDraftRevision = table.Column<int>(type: "integer", nullable: false),
                    Options = table.Column<string>(type: "jsonb", nullable: false),
                    AcknowledgedWarningCodes = table.Column<string>(type: "jsonb", nullable: false),
                    RequiredApprovalCount = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OpenedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    OpenArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    CloseArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    FinalizeArtifactId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionRecord", x => x.ElectionId);
                });

            migrationBuilder.CreateTable(
                name: "ElectionTrusteeInvitationRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    TrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    TrusteeDisplayName = table.Column<string>(type: "varchar(200)", nullable: true),
                    InvitedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    LinkedMessageId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "varchar(24)", nullable: false),
                    SentAtDraftRevision = table.Column<int>(type: "integer", nullable: false),
                    SentAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ResolvedAtDraftRevision = table.Column<int>(type: "integer", nullable: true),
                    RespondedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LatestTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    LatestBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionTrusteeInvitationRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionWarningAcknowledgementRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    WarningCode = table.Column<string>(type: "varchar(64)", nullable: false),
                    DraftRevision = table.Column<int>(type: "integer", nullable: false),
                    AcknowledgedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionWarningAcknowledgementRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionBoundaryArtifactRecord_ElectionId_ArtifactType",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                columns: new[] { "ElectionId", "ArtifactType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDraftSnapshotRecord_ElectionId_DraftRevision",
                schema: "Elections",
                table: "ElectionDraftSnapshotRecord",
                columns: new[] { "ElectionId", "DraftRevision" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRecord_GovernanceMode",
                schema: "Elections",
                table: "ElectionRecord",
                column: "GovernanceMode");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRecord_LastUpdatedAt",
                schema: "Elections",
                table: "ElectionRecord",
                column: "LastUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRecord_LifecycleState",
                schema: "Elections",
                table: "ElectionRecord",
                column: "LifecycleState");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRecord_OwnerPublicAddress",
                schema: "Elections",
                table: "ElectionRecord",
                column: "OwnerPublicAddress");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeInvitationRecord_ElectionId",
                schema: "Elections",
                table: "ElectionTrusteeInvitationRecord",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeInvitationRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionTrusteeInvitationRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeInvitationRecord_ElectionId_TrusteeUserAddre~",
                schema: "Elections",
                table: "ElectionTrusteeInvitationRecord",
                columns: new[] { "ElectionId", "TrusteeUserAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionWarningAcknowledgementRecord_ElectionId_WarningCode~",
                schema: "Elections",
                table: "ElectionWarningAcknowledgementRecord",
                columns: new[] { "ElectionId", "WarningCode", "DraftRevision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionBoundaryArtifactRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionDraftSnapshotRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionTrusteeInvitationRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionWarningAcknowledgementRecord",
                schema: "Elections");
        }
    }
}
