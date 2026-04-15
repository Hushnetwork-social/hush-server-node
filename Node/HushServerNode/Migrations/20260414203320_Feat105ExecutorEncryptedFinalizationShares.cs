using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat105ExecutorEncryptedFinalizationShares : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CloseCountingJobId",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExecutorKeyAlgorithm",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord",
                type: "varchar(64)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CloseCountingJobId",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord");

            migrationBuilder.DropColumn(
                name: "ExecutorKeyAlgorithm",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord");
        }
    }
}
