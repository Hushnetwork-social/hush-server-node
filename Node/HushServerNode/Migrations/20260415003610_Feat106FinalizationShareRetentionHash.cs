using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat106FinalizationShareRetentionHash : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ShareMaterialHash",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord",
                type: "varchar(128)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShareMaterialHash",
                schema: "Elections",
                table: "ElectionFinalizationShareRecord");
        }
    }
}
