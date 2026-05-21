using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat137RetentionLogPrivacySplit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_Organizat~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord");

            migrationBuilder.DropColumn(
                name: "FinalAcceptedBallotId",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord");

            migrationBuilder.DropColumn(
                name: "LinkedActorPublicAddress",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord");

            migrationBuilder.DropColumn(
                name: "OrganizationVoterId",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_Precommit~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "PrecommittedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_Precommit~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord");

            migrationBuilder.AddColumn<Guid>(
                name: "FinalAcceptedBallotId",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LinkedActorPublicAddress",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                type: "varchar(160)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "OrganizationVoterId",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                type: "varchar(128)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_Organizat~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "OrganizationVoterId", "PrecommittedAt" });
        }
    }
}
