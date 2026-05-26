using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat143DeploymentProofBindingLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionDeploymentProofCheckpointRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    CheckpointType = table.Column<string>(type: "varchar(48)", nullable: false),
                    SourceLifecycleState = table.Column<string>(type: "varchar(32)", nullable: false),
                    TargetLifecycleState = table.Column<string>(type: "varchar(32)", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    ClaimEffect = table.Column<string>(type: "varchar(40)", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ProviderStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    LedgerId = table.Column<Guid>(type: "uuid", nullable: false),
                    TransitionArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReportPackageId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProofSetId = table.Column<string>(type: "varchar(160)", nullable: true),
                    ProviderErrorCodes = table.Column<string>(type: "jsonb", nullable: false),
                    SupersedesCheckpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicSummary = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionDeploymentProofCheckpointRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionDeploymentProofComponentObservationRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ComponentId = table.Column<string>(type: "varchar(40)", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    ObservationSource = table.Column<string>(type: "varchar(40)", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeploymentProofId = table.Column<string>(type: "varchar(160)", nullable: true),
                    ExpectedDeploymentProofId = table.Column<string>(type: "varchar(160)", nullable: true),
                    ObservedDeploymentProofId = table.Column<string>(type: "varchar(160)", nullable: true),
                    ExpectedArtifactHash = table.Column<string>(type: "varchar(256)", nullable: true),
                    ObservedArtifactHash = table.Column<string>(type: "varchar(256)", nullable: true),
                    SourceRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    ArtifactHash = table.Column<string>(type: "varchar(256)", nullable: true),
                    PackageHash = table.Column<string>(type: "varchar(64)", nullable: true),
                    PublicPackageRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    MismatchCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    SupersedesProofIds = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionDeploymentProofComponentObservationRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionDeploymentProofEventRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ComponentId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Classification = table.Column<string>(type: "varchar(64)", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventPublicId = table.Column<string>(type: "varchar(160)", nullable: false),
                    EventType = table.Column<string>(type: "varchar(96)", nullable: false),
                    DeploymentRunId = table.Column<string>(type: "varchar(160)", nullable: true),
                    BeforeProofId = table.Column<string>(type: "varchar(160)", nullable: true),
                    AfterProofId = table.Column<string>(type: "varchar(160)", nullable: true),
                    Reason = table.Column<string>(type: "varchar(512)", nullable: true),
                    ChecksRerun = table.Column<string>(type: "jsonb", nullable: false),
                    CheckResult = table.Column<string>(type: "varchar(128)", nullable: true),
                    AccountabilityMarker = table.Column<string>(type: "varchar(256)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionDeploymentProofEventRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionDeploymentProofLedgerRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Status = table.Column<string>(type: "varchar(40)", nullable: false),
                    Visibility = table.Column<string>(type: "varchar(32)", nullable: false),
                    OpenedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ClosedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalizedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VoidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FinalStatus = table.Column<string>(type: "varchar(40)", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastReconciledAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LedgerPublicId = table.Column<string>(type: "varchar(160)", nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    DeploymentProfile = table.Column<string>(type: "varchar(128)", nullable: false),
                    DeploymentProtocolVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    PublicCatalogRepository = table.Column<string>(type: "varchar(512)", nullable: true),
                    PublicCatalogRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    PublicCatalogCommit = table.Column<string>(type: "varchar(128)", nullable: true),
                    PlatformCeremonyId = table.Column<string>(type: "varchar(160)", nullable: true),
                    ActiveProofSetIdAtOpen = table.Column<string>(type: "varchar(160)", nullable: true),
                    LatestCheckpointId = table.Column<Guid>(type: "uuid", nullable: true),
                    PublicLedgerArtifactRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    PublicLedgerArtifactHash = table.Column<string>(type: "varchar(64)", nullable: true),
                    RestrictedEvidenceIndexRef = table.Column<string>(type: "varchar(512)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionDeploymentProofLedgerRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionProofFamilyBindingStatusRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    EvidenceStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    ObservedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CheckpointId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProofFamilyId = table.Column<string>(type: "varchar(160)", nullable: false),
                    ProofFamilyVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    PackageId = table.Column<string>(type: "varchar(160)", nullable: true),
                    PackageHash = table.Column<string>(type: "varchar(64)", nullable: true),
                    PromotedRegisterRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    SourceFeature = table.Column<string>(type: "varchar(64)", nullable: false),
                    MismatchCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    PublicSummary = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionProofFamilyBindingStatusRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofCheckpointRecord_ElectionId_Checkpoi~",
                schema: "Elections",
                table: "ElectionDeploymentProofCheckpointRecord",
                columns: new[] { "ElectionId", "CheckpointType", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofCheckpointRecord_ElectionId_Observed~",
                schema: "Elections",
                table: "ElectionDeploymentProofCheckpointRecord",
                columns: new[] { "ElectionId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofCheckpointRecord_LedgerId",
                schema: "Elections",
                table: "ElectionDeploymentProofCheckpointRecord",
                column: "LedgerId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofCheckpointRecord_ReportPackageId",
                schema: "Elections",
                table: "ElectionDeploymentProofCheckpointRecord",
                column: "ReportPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofCheckpointRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionDeploymentProofCheckpointRecord",
                column: "SourceTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofCheckpointRecord_SupersedesCheckpoin~",
                schema: "Elections",
                table: "ElectionDeploymentProofCheckpointRecord",
                column: "SupersedesCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofCheckpointRecord_TransitionArtifactId",
                schema: "Elections",
                table: "ElectionDeploymentProofCheckpointRecord",
                column: "TransitionArtifactId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofComponentObservationRecord_Checkpoi~1",
                schema: "Elections",
                table: "ElectionDeploymentProofComponentObservationRecord",
                columns: new[] { "CheckpointId", "ComponentId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofComponentObservationRecord_Checkpoin~",
                schema: "Elections",
                table: "ElectionDeploymentProofComponentObservationRecord",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofComponentObservationRecord_Deploymen~",
                schema: "Elections",
                table: "ElectionDeploymentProofComponentObservationRecord",
                column: "DeploymentProofId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofComponentObservationRecord_ElectionI~",
                schema: "Elections",
                table: "ElectionDeploymentProofComponentObservationRecord",
                columns: new[] { "ElectionId", "ComponentId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofEventRecord_CheckpointId",
                schema: "Elections",
                table: "ElectionDeploymentProofEventRecord",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofEventRecord_CheckpointId_EventPublic~",
                schema: "Elections",
                table: "ElectionDeploymentProofEventRecord",
                columns: new[] { "CheckpointId", "EventPublicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofEventRecord_ElectionId_Classification",
                schema: "Elections",
                table: "ElectionDeploymentProofEventRecord",
                columns: new[] { "ElectionId", "Classification" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofEventRecord_ElectionId_OccurredAtUtc",
                schema: "Elections",
                table: "ElectionDeploymentProofEventRecord",
                columns: new[] { "ElectionId", "OccurredAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofLedgerRecord_ElectionId",
                schema: "Elections",
                table: "ElectionDeploymentProofLedgerRecord",
                column: "ElectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofLedgerRecord_LatestCheckpointId",
                schema: "Elections",
                table: "ElectionDeploymentProofLedgerRecord",
                column: "LatestCheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofLedgerRecord_LedgerPublicId",
                schema: "Elections",
                table: "ElectionDeploymentProofLedgerRecord",
                column: "LedgerPublicId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionDeploymentProofLedgerRecord_Status_LastReconciledAt~",
                schema: "Elections",
                table: "ElectionDeploymentProofLedgerRecord",
                columns: new[] { "Status", "LastReconciledAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionProofFamilyBindingStatusRecord_CheckpointId",
                schema: "Elections",
                table: "ElectionProofFamilyBindingStatusRecord",
                column: "CheckpointId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionProofFamilyBindingStatusRecord_CheckpointId_ProofFa~",
                schema: "Elections",
                table: "ElectionProofFamilyBindingStatusRecord",
                columns: new[] { "CheckpointId", "ProofFamilyId", "ProofFamilyVersion" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionProofFamilyBindingStatusRecord_ElectionId_ProofFami~",
                schema: "Elections",
                table: "ElectionProofFamilyBindingStatusRecord",
                columns: new[] { "ElectionId", "ProofFamilyId", "ObservedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionProofFamilyBindingStatusRecord_PackageHash",
                schema: "Elections",
                table: "ElectionProofFamilyBindingStatusRecord",
                column: "PackageHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionDeploymentProofCheckpointRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionDeploymentProofComponentObservationRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionDeploymentProofEventRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionDeploymentProofLedgerRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionProofFamilyBindingStatusRecord",
                schema: "Elections");
        }
    }
}
