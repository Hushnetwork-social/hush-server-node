using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Bank");

            migrationBuilder.EnsureSchema(
                name: "Blockchain");

            migrationBuilder.EnsureSchema(
                name: "Feeds");

            migrationBuilder.EnsureSchema(
                name: "Identity");

            migrationBuilder.CreateTable(
                name: "AddressBalance",
                schema: "Bank",
                columns: table => new
                {
                    PublicAddress = table.Column<string>(type: "text", nullable: false),
                    Token = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressBalance", x => new { x.PublicAddress, x.Token });
                });

            migrationBuilder.CreateTable(
                name: "BlockchainBlock",
                schema: "Blockchain",
                columns: table => new
                {
                    BlockId = table.Column<string>(type: "varchar(40)", nullable: false),
                    BlockIndex = table.Column<long>(type: "bigint", nullable: false),
                    PreviousBlockId = table.Column<string>(type: "varchar(40)", nullable: false),
                    NextBlockId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    BlockJson = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockchainBlock", x => x.BlockId);
                });

            migrationBuilder.CreateTable(
                name: "BlockchainState",
                schema: "Blockchain",
                columns: table => new
                {
                    BlockchainStateId = table.Column<string>(type: "varchar(40)", nullable: false),
                    BlockIndex = table.Column<long>(type: "bigint", nullable: false),
                    CurrentBlockId = table.Column<string>(type: "varchar(40)", nullable: false),
                    PreviousBlockId = table.Column<string>(type: "varchar(40)", nullable: false),
                    NextBlockId = table.Column<string>(type: "varchar(40)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockchainState", x => x.BlockchainStateId);
                });

            migrationBuilder.CreateTable(
                name: "Feed",
                schema: "Feeds",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false),
                    FeedType = table.Column<int>(type: "integer", nullable: false),
                    BlockIndex = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Feed", x => x.FeedId);
                });

            migrationBuilder.CreateTable(
                name: "FeedMessage",
                schema: "Feeds",
                columns: table => new
                {
                    FeedMessageId = table.Column<string>(type: "varchar(40)", nullable: false),
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    MessageContent = table.Column<string>(type: "text", nullable: false),
                    IssuerPublicAddress = table.Column<string>(type: "varchar(200)", nullable: false),
                    Timestamp = table.Column<string>(type: "varchar(40)", nullable: false),
                    BlockIndex = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedMessage", x => x.FeedMessageId);
                });

            migrationBuilder.CreateTable(
                name: "Profile",
                schema: "Identity",
                columns: table => new
                {
                    PublicSigningAddress = table.Column<string>(type: "text", nullable: false),
                    Alias = table.Column<string>(type: "text", nullable: false),
                    ShortAlias = table.Column<string>(type: "text", nullable: false),
                    PublicEncryptAddress = table.Column<string>(type: "text", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false),
                    BlockIndex = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profile", x => x.PublicSigningAddress);
                });

            migrationBuilder.CreateTable(
                name: "FeedParticipant",
                schema: "Feeds",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ParticipantPublicAddress = table.Column<string>(type: "varchar(500)", nullable: false),
                    ParticipantType = table.Column<int>(type: "integer", nullable: false),
                    EncryptedFeedKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedParticipant", x => new { x.FeedId, x.ParticipantPublicAddress });
                    table.ForeignKey(
                        name: "FK_FeedParticipant_Feed_FeedId",
                        column: x => x.FeedId,
                        principalSchema: "Feeds",
                        principalTable: "Feed",
                        principalColumn: "FeedId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddressBalance",
                schema: "Bank");

            migrationBuilder.DropTable(
                name: "BlockchainBlock",
                schema: "Blockchain");

            migrationBuilder.DropTable(
                name: "BlockchainState",
                schema: "Blockchain");

            migrationBuilder.DropTable(
                name: "FeedMessage",
                schema: "Feeds");

            migrationBuilder.DropTable(
                name: "FeedParticipant",
                schema: "Feeds");

            migrationBuilder.DropTable(
                name: "Profile",
                schema: "Identity");

            migrationBuilder.DropTable(
                name: "Feed",
                schema: "Feeds");
        }
    }
}
