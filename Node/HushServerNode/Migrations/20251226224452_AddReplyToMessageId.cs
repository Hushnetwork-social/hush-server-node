using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddReplyToMessageId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReplyToMessageId",
                schema: "Feeds",
                table: "FeedMessage",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_FeedMessage_ReplyToMessageId",
                schema: "Feeds",
                table: "FeedMessage",
                column: "ReplyToMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedMessage_ReplyToMessageId",
                schema: "Feeds",
                table: "FeedMessage");

            migrationBuilder.DropColumn(
                name: "ReplyToMessageId",
                schema: "Feeds",
                table: "FeedMessage");
        }
    }
}
