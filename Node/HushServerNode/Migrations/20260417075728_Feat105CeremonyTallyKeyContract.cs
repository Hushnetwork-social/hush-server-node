using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat105CeremonyTallyKeyContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "TallyPublicKey",
                schema: "Elections",
                table: "ElectionCeremonyVersionRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "CloseCountingPublicCommitment",
                schema: "Elections",
                table: "ElectionCeremonyTrusteeStateRecord",
                type: "bytea",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "TallyPublicKey",
                schema: "Elections",
                table: "ElectionCeremonyVersionRecord");

            migrationBuilder.DropColumn(
                name: "CloseCountingPublicCommitment",
                schema: "Elections",
                table: "ElectionCeremonyTrusteeStateRecord");
        }
    }
}
