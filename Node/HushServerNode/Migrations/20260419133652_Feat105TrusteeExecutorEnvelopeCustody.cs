using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat105TrusteeExecutorEnvelopeCustody : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SealAlgorithm",
                schema: "Elections",
                table: "ElectionExecutorSessionKeyEnvelopeRecord",
                type: "varchar(96)",
                nullable: false,
                defaultValue: "node-encrypt-address-v1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SealAlgorithm",
                schema: "Elections",
                table: "ElectionExecutorSessionKeyEnvelopeRecord");
        }
    }
}
