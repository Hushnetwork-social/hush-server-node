using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushNode.Elections.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Feat146GovernedOutcomeProducer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionGovernedOutcomeDecisionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    AuthorityRole = table.Column<string>(type: "varchar(64)", nullable: false),
                    AuthoritySource = table.Column<string>(type: "varchar(128)", nullable: false),
                    Feat140HandoffRef = table.Column<string>(type: "varchar(512)", nullable: false),
                    Feat140HandoffHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    AuthorityDecisionRef = table.Column<string>(type: "varchar(512)", nullable: false),
                    AuthorityDecisionHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    GovernanceRuleRef = table.Column<string>(type: "varchar(512)", nullable: false),
                    FinalityRuleRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    RemedyRuleRef = table.Column<string>(type: "varchar(512)", nullable: true),
                    CloseArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    TallyReadyArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    UnofficialResultArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficialResultArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfficialResultSourceArtifactId = table.Column<Guid>(type: "uuid", nullable: false),
                    FinalizeArtifactId = table.Column<Guid>(type: "uuid", nullable: true),
                    MissingFinalizeEvidenceRefs = table.Column<string>(type: "jsonb", nullable: false),
                    ContinuityIncidentEvidenceRefs = table.Column<string>(type: "jsonb", nullable: false),
                    AvailableTrusteeAcknowledgementRefs = table.Column<string>(type: "jsonb", nullable: false),
                    KeyLostTrusteeDecisionIds = table.Column<string>(type: "jsonb", nullable: false),
                    PublicSummary = table.Column<string>(type: "text", nullable: false),
                    DecisionType = table.Column<string>(type: "varchar(64)", nullable: false),
                    OutcomeStatus = table.Column<string>(type: "varchar(40)", nullable: false),
                    CleanFinalization = table.Column<bool>(type: "boolean", nullable: false),
                    FinalizationMode = table.Column<string>(type: "varchar(40)", nullable: false),
                    PreviousLifecycleState = table.Column<string>(type: "varchar(32)", nullable: false),
                    ResultingLifecycleState = table.Column<string>(type: "varchar(32)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionGovernedOutcomeDecisionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionTrusteeContinuityDecisionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    DecidedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RecordedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    TrusteePublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    TrusteeDisplayName = table.Column<string>(type: "varchar(160)", nullable: true),
                    ContinuityStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    AuthorityDecisionRef = table.Column<string>(type: "varchar(512)", nullable: false),
                    AuthorityDecisionHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    GovernanceRuleRef = table.Column<string>(type: "varchar(512)", nullable: false),
                    ContinuityEvidenceRefs = table.Column<string>(type: "jsonb", nullable: false),
                    RecordedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionTrusteeContinuityDecisionRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedOutcomeDecisionRecord_AuthorityDecisionHash",
                schema: "Elections",
                table: "ElectionGovernedOutcomeDecisionRecord",
                column: "AuthorityDecisionHash");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedOutcomeDecisionRecord_ElectionId",
                schema: "Elections",
                table: "ElectionGovernedOutcomeDecisionRecord",
                column: "ElectionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedOutcomeDecisionRecord_ElectionId_DecidedAtU~",
                schema: "Elections",
                table: "ElectionGovernedOutcomeDecisionRecord",
                columns: new[] { "ElectionId", "DecidedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedOutcomeDecisionRecord_ElectionId_OutcomeSta~",
                schema: "Elections",
                table: "ElectionGovernedOutcomeDecisionRecord",
                columns: new[] { "ElectionId", "OutcomeStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedOutcomeDecisionRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionGovernedOutcomeDecisionRecord",
                column: "SourceTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeContinuityDecisionRecord_AuthorityDecisionHa~",
                schema: "Elections",
                table: "ElectionTrusteeContinuityDecisionRecord",
                column: "AuthorityDecisionHash");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeContinuityDecisionRecord_ElectionId",
                schema: "Elections",
                table: "ElectionTrusteeContinuityDecisionRecord",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeContinuityDecisionRecord_ElectionId_Continui~",
                schema: "Elections",
                table: "ElectionTrusteeContinuityDecisionRecord",
                columns: new[] { "ElectionId", "ContinuityStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeContinuityDecisionRecord_ElectionId_TrusteeP~",
                schema: "Elections",
                table: "ElectionTrusteeContinuityDecisionRecord",
                columns: new[] { "ElectionId", "TrusteePublicAddress", "ContinuityStatus" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionTrusteeContinuityDecisionRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionTrusteeContinuityDecisionRecord",
                column: "SourceTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionGovernedOutcomeDecisionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionTrusteeContinuityDecisionRecord",
                schema: "Elections");
        }
    }
}
