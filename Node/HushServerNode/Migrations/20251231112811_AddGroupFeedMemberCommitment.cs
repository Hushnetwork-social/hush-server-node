using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupFeedMemberCommitment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupFeedMemberCommitment",
                schema: "Feeds",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    UserCommitment = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    KeyGeneration = table.Column<int>(type: "int", nullable: false),
                    RegisteredAtBlock = table.Column<long>(type: "bigint", nullable: false),
                    RevokedAtBlock = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupFeedMemberCommitment", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupFeedMemberCommitment_FeedId_RevokedAtBlock",
                schema: "Feeds",
                table: "GroupFeedMemberCommitment",
                columns: new[] { "FeedId", "RevokedAtBlock" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupFeedMemberCommitment_FeedId_UserCommitment",
                schema: "Feeds",
                table: "GroupFeedMemberCommitment",
                columns: new[] { "FeedId", "UserCommitment" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupFeedMemberCommitment",
                schema: "Feeds");
        }
    }
}
