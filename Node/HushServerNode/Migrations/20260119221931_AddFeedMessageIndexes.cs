using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedMessageIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_FeedMessage_FeedId_BlockIndex",
                schema: "Feeds",
                table: "FeedMessage",
                columns: new[] { "FeedId", "BlockIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_FeedMessage_IssuerPublicAddress_BlockIndex",
                schema: "Feeds",
                table: "FeedMessage",
                columns: new[] { "IssuerPublicAddress", "BlockIndex" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_FeedMessage_FeedId_BlockIndex",
                schema: "Feeds",
                table: "FeedMessage");

            migrationBuilder.DropIndex(
                name: "IX_FeedMessage_IssuerPublicAddress_BlockIndex",
                schema: "Feeds",
                table: "FeedMessage");
        }
    }
}
