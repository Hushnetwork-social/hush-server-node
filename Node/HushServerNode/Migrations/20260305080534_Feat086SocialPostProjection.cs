using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat086SocialPostProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialPost",
                schema: "Feeds",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorPublicAddress = table.Column<string>(type: "varchar(500)", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    AudienceVisibility = table.Column<int>(type: "int", nullable: false),
                    CreatedAtBlock = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPost", x => x.PostId);
                });

            migrationBuilder.CreateTable(
                name: "SocialPostAudienceCircle",
                schema: "Feeds",
                columns: table => new
                {
                    PostId = table.Column<Guid>(type: "uuid", nullable: false),
                    CircleFeedId = table.Column<string>(type: "varchar(40)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostAudienceCircle", x => new { x.PostId, x.CircleFeedId });
                    table.ForeignKey(
                        name: "FK_SocialPostAudienceCircle_SocialPost_PostId",
                        column: x => x.PostId,
                        principalSchema: "Feeds",
                        principalTable: "SocialPost",
                        principalColumn: "PostId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPost_AuthorPublicAddress",
                schema: "Feeds",
                table: "SocialPost",
                column: "AuthorPublicAddress");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPost_CreatedAtBlock",
                schema: "Feeds",
                table: "SocialPost",
                column: "CreatedAtBlock");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostAudienceCircle_CircleFeedId",
                schema: "Feeds",
                table: "SocialPostAudienceCircle",
                column: "CircleFeedId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPostAudienceCircle",
                schema: "Feeds");

            migrationBuilder.DropTable(
                name: "SocialPost",
                schema: "Feeds");
        }
    }
}
