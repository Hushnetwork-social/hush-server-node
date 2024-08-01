using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.CacheService.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedMessagestable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedMessageEntity",
                columns: table => new
                {
                    FeedMessageId = table.Column<string>(type: "text", nullable: false),
                    FeedId = table.Column<string>(type: "text", nullable: false),
                    MessageContent = table.Column<string>(type: "text", nullable: false),
                    IssuerPublicAddress = table.Column<string>(type: "text", nullable: false),
                    IssuerName = table.Column<string>(type: "text", nullable: false),
                    TimeStamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    BlockIndex = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedMessageEntity", x => x.FeedMessageId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedMessageEntity");
        }
    }
}
