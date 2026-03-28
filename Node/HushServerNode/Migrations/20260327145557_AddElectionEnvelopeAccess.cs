using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddElectionEnvelopeAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionEnvelopeAccessRecord",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ActorEncryptedElectionPrivateKey = table.Column<string>(type: "text", nullable: false),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionEnvelopeAccessRecord", x => new { x.ElectionId, x.ActorPublicAddress });
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEnvelopeAccessRecord_ActorPublicAddress",
                schema: "Elections",
                table: "ElectionEnvelopeAccessRecord",
                column: "ActorPublicAddress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionEnvelopeAccessRecord",
                schema: "Elections");
        }
    }
}
