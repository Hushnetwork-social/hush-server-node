using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.CacheService.Migrations
{
    /// <inheritdoc />
    public partial class Initialdatabasedraft : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AddressBalance",
                columns: table => new
                {
                    Address = table.Column<string>(type: "text", nullable: false),
                    Balance = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddressBalance", x => x.Address);
                });

            migrationBuilder.CreateTable(
                name: "BlockchainState",
                columns: table => new
                {
                    BlockchainStateId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastBlockIndex = table.Column<long>(type: "bigint", nullable: false),
                    CurrentBlockId = table.Column<string>(type: "text", nullable: false),
                    CurrentPreviousBlockId = table.Column<string>(type: "text", nullable: false),
                    CurrentNextBlockId = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockchainState", x => x.BlockchainStateId);
                });

            migrationBuilder.CreateTable(
                name: "BlockEntity",
                columns: table => new
                {
                    BlockId = table.Column<string>(type: "text", nullable: false),
                    Height = table.Column<long>(type: "bigint", nullable: false),
                    PreviousBlockId = table.Column<string>(type: "text", nullable: false),
                    NextBlockId = table.Column<string>(type: "text", nullable: false),
                    Hash = table.Column<string>(type: "text", nullable: false),
                    BlockJson = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlockEntity", x => x.BlockId);
                });

            migrationBuilder.CreateTable(
                name: "FeedEntity",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "text", nullable: false),
                    Title = table.Column<string>(type: "text", nullable: false),
                    FeedType = table.Column<int>(type: "integer", nullable: false),
                    BlockIndex = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedEntity", x => x.FeedId);
                });

            migrationBuilder.CreateTable(
                name: "Profile",
                columns: table => new
                {
                    PublicSigningAddress = table.Column<string>(type: "text", nullable: false),
                    PublicEncryptAddress = table.Column<string>(type: "text", nullable: false),
                    UserName = table.Column<string>(type: "text", nullable: false),
                    IsPublic = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Profile", x => x.PublicSigningAddress);
                });

            migrationBuilder.CreateTable(
                name: "FeedParticipants",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "text", nullable: false),
                    ParticipantPublicAddress = table.Column<string>(type: "text", nullable: false),
                    ParticipantType = table.Column<int>(type: "integer", nullable: false),
                    PublicEncryptAddress = table.Column<string>(type: "text", nullable: false),
                    PrivateEncryptKey = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedParticipants", x => new { x.FeedId, x.ParticipantPublicAddress });
                    table.ForeignKey(
                        name: "FK_FeedParticipants_FeedEntity_FeedId",
                        column: x => x.FeedId,
                        principalTable: "FeedEntity",
                        principalColumn: "FeedId",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AddressBalance");

            migrationBuilder.DropTable(
                name: "BlockchainState");

            migrationBuilder.DropTable(
                name: "BlockEntity");

            migrationBuilder.DropTable(
                name: "FeedParticipants");

            migrationBuilder.DropTable(
                name: "Profile");

            migrationBuilder.DropTable(
                name: "FeedEntity");
        }
    }
}
