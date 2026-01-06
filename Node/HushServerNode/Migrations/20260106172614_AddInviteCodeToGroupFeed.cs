using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddInviteCodeToGroupFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                schema: "Feeds",
                table: "GroupFeed",
                type: "varchar(12)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupFeed_InviteCode",
                schema: "Feeds",
                table: "GroupFeed",
                column: "InviteCode",
                unique: true,
                filter: "\"InviteCode\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GroupFeed_InviteCode",
                schema: "Feeds",
                table: "GroupFeed");

            migrationBuilder.DropColumn(
                name: "InviteCode",
                schema: "Feeds",
                table: "GroupFeed");
        }
    }
}
