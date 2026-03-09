using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddReactionContextToSocialPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "AuthorCommitment",
                schema: "Feeds",
                table: "SocialPost",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ReactionScopeId",
                schema: "Feeds",
                table: "SocialPost",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_SocialPost_ReactionScopeId",
                schema: "Feeds",
                table: "SocialPost",
                column: "ReactionScopeId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SocialPost_ReactionScopeId",
                schema: "Feeds",
                table: "SocialPost");

            migrationBuilder.DropColumn(
                name: "AuthorCommitment",
                schema: "Feeds",
                table: "SocialPost");

            migrationBuilder.DropColumn(
                name: "ReactionScopeId",
                schema: "Feeds",
                table: "SocialPost");
        }
    }
}
