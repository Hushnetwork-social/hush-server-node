using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupFeedTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupFeed",
                schema: "Feeds",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    CreatedAtBlock = table.Column<long>(type: "bigint", nullable: false),
                    CurrentKeyGeneration = table.Column<int>(type: "int", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupFeed", x => x.FeedId);
                });

            migrationBuilder.CreateTable(
                name: "GroupFeedKeyGeneration",
                schema: "Feeds",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    KeyGeneration = table.Column<int>(type: "int", nullable: false),
                    ValidFromBlock = table.Column<long>(type: "bigint", nullable: false),
                    RotationTrigger = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupFeedKeyGeneration", x => new { x.FeedId, x.KeyGeneration });
                    table.ForeignKey(
                        name: "FK_GroupFeedKeyGeneration_GroupFeed_FeedId",
                        column: x => x.FeedId,
                        principalSchema: "Feeds",
                        principalTable: "GroupFeed",
                        principalColumn: "FeedId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupFeedParticipant",
                schema: "Feeds",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ParticipantPublicAddress = table.Column<string>(type: "varchar(500)", nullable: false),
                    ParticipantType = table.Column<int>(type: "int", nullable: false),
                    JoinedAtBlock = table.Column<long>(type: "bigint", nullable: false),
                    LeftAtBlock = table.Column<long>(type: "bigint", nullable: true),
                    LastLeaveBlock = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupFeedParticipant", x => new { x.FeedId, x.ParticipantPublicAddress });
                    table.ForeignKey(
                        name: "FK_GroupFeedParticipant_GroupFeed_FeedId",
                        column: x => x.FeedId,
                        principalSchema: "Feeds",
                        principalTable: "GroupFeed",
                        principalColumn: "FeedId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupFeedEncryptedKey",
                schema: "Feeds",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    KeyGeneration = table.Column<int>(type: "int", nullable: false),
                    MemberPublicAddress = table.Column<string>(type: "varchar(500)", nullable: false),
                    EncryptedAesKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupFeedEncryptedKey", x => new { x.FeedId, x.KeyGeneration, x.MemberPublicAddress });
                    table.ForeignKey(
                        name: "FK_GroupFeedEncryptedKey_GroupFeedKeyGeneration_FeedId_KeyGene~",
                        columns: x => new { x.FeedId, x.KeyGeneration },
                        principalSchema: "Feeds",
                        principalTable: "GroupFeedKeyGeneration",
                        principalColumns: new[] { "FeedId", "KeyGeneration" },
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupFeedEncryptedKey",
                schema: "Feeds");

            migrationBuilder.DropTable(
                name: "GroupFeedParticipant",
                schema: "Feeds");

            migrationBuilder.DropTable(
                name: "GroupFeedKeyGeneration",
                schema: "Feeds");

            migrationBuilder.DropTable(
                name: "GroupFeed",
                schema: "Feeds");
        }
    }
}
