using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat116Sp06ControlProfileProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ControlDomainProfileId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ControlDomainProfileVersion",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(32)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThresholdProfileId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ControlDomainProfileId",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "ControlDomainProfileVersion",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "ThresholdProfileId",
                schema: "Elections",
                table: "ElectionRecord");
        }
    }
}
