using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat085PendingModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsInnerCircle",
                schema: "Feeds",
                table: "GroupFeed",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "OwnerPublicAddress",
                schema: "Feeds",
                table: "GroupFeed",
                type: "varchar(500)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupFeed_OwnerPublicAddress_IsInnerCircle",
                schema: "Feeds",
                table: "GroupFeed",
                columns: new[] { "OwnerPublicAddress", "IsInnerCircle" },
                unique: true,
                filter: "\"IsInnerCircle\" = TRUE AND \"OwnerPublicAddress\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GroupFeed_OwnerPublicAddress_IsInnerCircle",
                schema: "Feeds",
                table: "GroupFeed");

            migrationBuilder.DropColumn(
                name: "IsInnerCircle",
                schema: "Feeds",
                table: "GroupFeed");

            migrationBuilder.DropColumn(
                name: "OwnerPublicAddress",
                schema: "Feeds",
                table: "GroupFeed");
        }
    }
}
