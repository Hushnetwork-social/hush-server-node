using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat105SelectedProfileContract : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SelectedProfileId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SelectedProfileDevOnly",
                schema: "Elections",
                table: "ElectionRecord",
                type: "boolean",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE "Elections"."ElectionRecord"
                SET "SelectedProfileId" =
                    CASE
                        WHEN "BindingStatus" = 'NonBinding' THEN 'dkg-dev-3of5'
                        ELSE 'dkg-prod-3of5'
                    END,
                    "SelectedProfileDevOnly" =
                    CASE
                        WHEN "BindingStatus" = 'NonBinding' THEN TRUE
                        ELSE FALSE
                    END
                WHERE "SelectedProfileId" IS NULL
                   OR btrim("SelectedProfileId") = ''
                   OR "SelectedProfileDevOnly" IS NULL;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "SelectedProfileId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<bool>(
                name: "SelectedProfileDevOnly",
                schema: "Elections",
                table: "ElectionRecord",
                type: "boolean",
                nullable: false,
                oldClrType: typeof(bool),
                oldType: "boolean",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SelectedProfileDevOnly",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "SelectedProfileId",
                schema: "Elections",
                table: "ElectionRecord");
        }
    }
}
