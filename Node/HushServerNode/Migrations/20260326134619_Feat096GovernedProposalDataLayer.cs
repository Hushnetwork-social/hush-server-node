using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat096GovernedProposalDataLayer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionGovernedProposalApprovalRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProposalId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ActionType = table.Column<string>(type: "varchar(24)", nullable: false),
                    LifecycleStateAtProposalCreation = table.Column<string>(type: "varchar(24)", nullable: false),
                    TrusteeUserAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    TrusteeDisplayName = table.Column<string>(type: "varchar(200)", nullable: true),
                    ApprovalNote = table.Column<string>(type: "text", nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionGovernedProposalApprovalRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionGovernedProposalRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ActionType = table.Column<string>(type: "varchar(24)", nullable: false),
                    LifecycleStateAtCreation = table.Column<string>(type: "varchar(24)", nullable: false),
                    ProposedByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExecutionStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    LastExecutionAttemptedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExecutionFailureReason = table.Column<string>(type: "text", nullable: true),
                    LastExecutionTriggeredByPublicAddress = table.Column<string>(type: "varchar(160)", nullable: true),
                    LatestTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    LatestBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    LatestBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionGovernedProposalRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedProposalApprovalRecord_ElectionId_ActionType",
                schema: "Elections",
                table: "ElectionGovernedProposalApprovalRecord",
                columns: new[] { "ElectionId", "ActionType" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedProposalApprovalRecord_ProposalId",
                schema: "Elections",
                table: "ElectionGovernedProposalApprovalRecord",
                column: "ProposalId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedProposalApprovalRecord_ProposalId_TrusteeUs~",
                schema: "Elections",
                table: "ElectionGovernedProposalApprovalRecord",
                columns: new[] { "ProposalId", "TrusteeUserAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedProposalRecord_ElectionId",
                schema: "Elections",
                table: "ElectionGovernedProposalRecord",
                column: "ElectionId");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedProposalRecord_ElectionId_CreatedAt",
                schema: "Elections",
                table: "ElectionGovernedProposalRecord",
                columns: new[] { "ElectionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionGovernedProposalRecord_ElectionId_ExecutionStatus",
                schema: "Elections",
                table: "ElectionGovernedProposalRecord",
                columns: new[] { "ElectionId", "ExecutionStatus" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionGovernedProposalApprovalRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionGovernedProposalRecord",
                schema: "Elections");
        }
    }
}
