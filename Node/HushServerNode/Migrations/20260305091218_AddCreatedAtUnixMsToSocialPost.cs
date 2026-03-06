using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedAtUnixMsToSocialPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "CreatedAtUnixMs",
                schema: "Feeds",
                table: "SocialPost",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_SocialPost_CreatedAtUnixMs",
                schema: "Feeds",
                table: "SocialPost",
                column: "CreatedAtUnixMs");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SocialPost_CreatedAtUnixMs",
                schema: "Feeds",
                table: "SocialPost");

            migrationBuilder.DropColumn(
                name: "CreatedAtUnixMs",
                schema: "Feeds",
                table: "SocialPost");
        }
    }
}
