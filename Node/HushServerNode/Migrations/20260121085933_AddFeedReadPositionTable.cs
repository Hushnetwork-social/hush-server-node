using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedReadPositionTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FeedReadPosition",
                schema: "Feeds",
                columns: table => new
                {
                    UserId = table.Column<string>(type: "varchar(500)", nullable: false),
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    LastReadBlockIndex = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedReadPosition", x => new { x.UserId, x.FeedId });
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedReadPosition_UserId",
                schema: "Feeds",
                table: "FeedReadPosition",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedReadPosition",
                schema: "Feeds");
        }
    }
}
