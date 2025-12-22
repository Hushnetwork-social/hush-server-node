using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddVersionToMessageReactionTally : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Version",
                schema: "Reactions",
                table: "MessageReactionTally",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateIndex(
                name: "IX_MessageReactionTally_FeedId_Version",
                schema: "Reactions",
                table: "MessageReactionTally",
                columns: new[] { "FeedId", "Version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_MessageReactionTally_FeedId_Version",
                schema: "Reactions",
                table: "MessageReactionTally");

            migrationBuilder.DropColumn(
                name: "Version",
                schema: "Reactions",
                table: "MessageReactionTally");
        }
    }
}
