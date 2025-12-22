using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class ProtocolOmega : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "Reactions");

            migrationBuilder.AddColumn<byte[]>(
                name: "AuthorCommitment",
                schema: "Feeds",
                table: "FeedMessage",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FeedMemberCommitment",
                schema: "Reactions",
                columns: table => new
                {
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    UserCommitment = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeedMemberCommitment", x => new { x.FeedId, x.UserCommitment });
                });

            migrationBuilder.CreateTable(
                name: "MerkleRootHistory",
                schema: "Reactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    MerkleRoot = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    BlockHeight = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MerkleRootHistory", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MessageReactionTally",
                schema: "Reactions",
                columns: table => new
                {
                    MessageId = table.Column<string>(type: "varchar(40)", nullable: false),
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    TallyC1X = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    TallyC1Y = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    TallyC2X = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    TallyC2Y = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    TotalCount = table.Column<int>(type: "integer", nullable: false),
                    LastUpdated = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MessageReactionTally", x => x.MessageId);
                });

            migrationBuilder.CreateTable(
                name: "ReactionNullifier",
                schema: "Reactions",
                columns: table => new
                {
                    Nullifier = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    MessageId = table.Column<string>(type: "varchar(40)", nullable: false),
                    VoteC1X = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    VoteC1Y = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    VoteC2X = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    VoteC2Y = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    EncryptedEmojiBackup = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReactionNullifier", x => x.Nullifier);
                });

            migrationBuilder.CreateTable(
                name: "ReactionTransaction",
                schema: "Reactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BlockHeight = table.Column<long>(type: "bigint", nullable: false),
                    FeedId = table.Column<string>(type: "varchar(40)", nullable: false),
                    MessageId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Nullifier = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CiphertextC1X = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    CiphertextC1Y = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    CiphertextC2X = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    CiphertextC2Y = table.Column<byte[][]>(type: "bytea[]", nullable: false),
                    ZkProof = table.Column<byte[]>(type: "bytea", nullable: false),
                    CircuitVersion = table.Column<string>(type: "varchar(20)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReactionTransaction", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FeedMemberCommitment_FeedId",
                schema: "Reactions",
                table: "FeedMemberCommitment",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_FeedMemberCommitment_RegisteredAt",
                schema: "Reactions",
                table: "FeedMemberCommitment",
                column: "RegisteredAt");

            migrationBuilder.CreateIndex(
                name: "IX_MerkleRootHistory_BlockHeight",
                schema: "Reactions",
                table: "MerkleRootHistory",
                column: "BlockHeight");

            migrationBuilder.CreateIndex(
                name: "IX_MerkleRootHistory_FeedId_CreatedAt",
                schema: "Reactions",
                table: "MerkleRootHistory",
                columns: new[] { "FeedId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_MessageReactionTally_FeedId",
                schema: "Reactions",
                table: "MessageReactionTally",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_MessageReactionTally_LastUpdated",
                schema: "Reactions",
                table: "MessageReactionTally",
                column: "LastUpdated");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionNullifier_MessageId",
                schema: "Reactions",
                table: "ReactionNullifier",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionNullifier_UpdatedAt",
                schema: "Reactions",
                table: "ReactionNullifier",
                column: "UpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionTransaction_BlockHeight",
                schema: "Reactions",
                table: "ReactionTransaction",
                column: "BlockHeight");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionTransaction_FeedId",
                schema: "Reactions",
                table: "ReactionTransaction",
                column: "FeedId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionTransaction_MessageId",
                schema: "Reactions",
                table: "ReactionTransaction",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ReactionTransaction_Nullifier",
                schema: "Reactions",
                table: "ReactionTransaction",
                column: "Nullifier");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FeedMemberCommitment",
                schema: "Reactions");

            migrationBuilder.DropTable(
                name: "MerkleRootHistory",
                schema: "Reactions");

            migrationBuilder.DropTable(
                name: "MessageReactionTally",
                schema: "Reactions");

            migrationBuilder.DropTable(
                name: "ReactionNullifier",
                schema: "Reactions");

            migrationBuilder.DropTable(
                name: "ReactionTransaction",
                schema: "Reactions");

            migrationBuilder.DropColumn(
                name: "AuthorCommitment",
                schema: "Feeds",
                table: "FeedMessage");
        }
    }
}
