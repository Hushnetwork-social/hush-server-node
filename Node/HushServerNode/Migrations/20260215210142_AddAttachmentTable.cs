using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class AddAttachmentTable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Attachment",
                schema: "Feeds",
                columns: table => new
                {
                    Id = table.Column<string>(type: "varchar(40)", nullable: false),
                    EncryptedOriginal = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedThumbnail = table.Column<byte[]>(type: "bytea", nullable: true),
                    FeedMessageId = table.Column<string>(type: "varchar(40)", nullable: false),
                    OriginalSize = table.Column<long>(type: "bigint", nullable: false),
                    ThumbnailSize = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "varchar(100)", nullable: false),
                    FileName = table.Column<string>(type: "varchar(255)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachment", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_FeedMessageId",
                schema: "Feeds",
                table: "Attachment",
                column: "FeedMessageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Attachment",
                schema: "Feeds");
        }
    }
}
