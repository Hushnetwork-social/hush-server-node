using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushNode.Elections.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Feat105AdminOnlyProtectedTallyEnvelope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                schema: "Elections",
                columns: table => new
                {
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    SelectedProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    TallyPublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    TallyPublicKeyFingerprint = table.Column<string>(type: "varchar(256)", nullable: false),
                    SealedTallyPrivateScalar = table.Column<string>(type: "text", nullable: false),
                    ScalarEncoding = table.Column<string>(type: "varchar(96)", nullable: false),
                    SealAlgorithm = table.Column<string>(type: "varchar(96)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DestroyedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SealedByServiceIdentity = table.Column<string>(type: "varchar(160)", nullable: true),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAdminOnlyProtectedTallyEnvelopeRecord", x => x.ElectionId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                schema: "Elections");
        }
    }
}
