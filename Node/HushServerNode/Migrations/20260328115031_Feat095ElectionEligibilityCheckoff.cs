using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat095ElectionEligibilityCheckoff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionEligibilityActivationEventRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    AttemptedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    Outcome = table.Column<string>(type: "varchar(16)", nullable: false),
                    BlockReason = table.Column<string>(type: "varchar(40)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionEligibilityActivationEventRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionEligibilitySnapshotRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    SnapshotType = table.Column<string>(type: "varchar(16)", nullable: false),
                    EligibilityMutationPolicy = table.Column<string>(type: "varchar(96)", nullable: false),
                    RosteredCount = table.Column<int>(type: "integer", nullable: false),
                    LinkedCount = table.Column<int>(type: "integer", nullable: false),
                    ActiveDenominatorCount = table.Column<int>(type: "integer", nullable: false),
                    CountedParticipationCount = table.Column<int>(type: "integer", nullable: false),
                    BlankCount = table.Column<int>(type: "integer", nullable: false),
                    DidNotVoteCount = table.Column<int>(type: "integer", nullable: false),
                    RosteredVoterSetHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    ActiveDenominatorSetHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CountedParticipationSetHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    BoundaryArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionEligibilitySnapshotRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionParticipationRecord",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    ParticipationStatus = table.Column<string>(type: "varchar(24)", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LatestTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    LatestBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionParticipationRecord", x => new { x.ElectionId, x.OrganizationVoterId });
                });

            migrationBuilder.CreateTable(
                name: "ElectionRosterEntryRecord",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContactType = table.Column<string>(type: "varchar(16)", nullable: false),
                    ContactValue = table.Column<string>(type: "varchar(320)", nullable: false),
                    LinkStatus = table.Column<string>(type: "varchar(16)", nullable: false),
                    LinkedActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: true),
                    LinkedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    VotingRightStatus = table.Column<string>(type: "varchar(16)", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WasPresentAtOpen = table.Column<bool>(type: "boolean", nullable: false),
                    WasActiveAtOpen = table.Column<bool>(type: "boolean", nullable: false),
                    LastActivatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastActivatedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LatestTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    LatestBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionRosterEntryRecord", x => new { x.ElectionId, x.OrganizationVoterId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEligibilityActivationEventRecord_ElectionId_Occurre~",
                schema: "Elections",
                table: "ElectionEligibilityActivationEventRecord",
                columns: new[] { "ElectionId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEligibilityActivationEventRecord_ElectionId_Organiz~",
                schema: "Elections",
                table: "ElectionEligibilityActivationEventRecord",
                columns: new[] { "ElectionId", "OrganizationVoterId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEligibilityActivationEventRecord_ElectionId_Outcome",
                schema: "Elections",
                table: "ElectionEligibilityActivationEventRecord",
                columns: new[] { "ElectionId", "Outcome" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEligibilitySnapshotRecord_ElectionId_RecordedAt",
                schema: "Elections",
                table: "ElectionEligibilitySnapshotRecord",
                columns: new[] { "ElectionId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEligibilitySnapshotRecord_ElectionId_SnapshotType",
                schema: "Elections",
                table: "ElectionEligibilitySnapshotRecord",
                columns: new[] { "ElectionId", "SnapshotType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionParticipationRecord_ElectionId_LastUpdatedAt",
                schema: "Elections",
                table: "ElectionParticipationRecord",
                columns: new[] { "ElectionId", "LastUpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionParticipationRecord_ElectionId_ParticipationStatus",
                schema: "Elections",
                table: "ElectionParticipationRecord",
                columns: new[] { "ElectionId", "ParticipationStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_LinkedActorPublicAddre~",
                schema: "Elections",
                table: "ElectionRosterEntryRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_LinkStatus",
                schema: "Elections",
                table: "ElectionRosterEntryRecord",
                columns: new[] { "ElectionId", "LinkStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_VotingRightStatus",
                schema: "Elections",
                table: "ElectionRosterEntryRecord",
                columns: new[] { "ElectionId", "VotingRightStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_WasPresentAtOpen_Votin~",
                schema: "Elections",
                table: "ElectionRosterEntryRecord",
                columns: new[] { "ElectionId", "WasPresentAtOpen", "VotingRightStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionEligibilityActivationEventRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionEligibilitySnapshotRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionParticipationRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionRosterEntryRecord",
                schema: "Elections");
        }
    }
}
