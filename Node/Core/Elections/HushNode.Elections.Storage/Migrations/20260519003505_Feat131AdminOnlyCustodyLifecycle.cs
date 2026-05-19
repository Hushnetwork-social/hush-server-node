using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushNode.Elections.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Feat131AdminOnlyCustodyLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CustodyActionServiceIdentity",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(160)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyExceptionId",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(160)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyLastAction",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(96)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyLastErrorCode",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyLastErrorMessage",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyLifecycleState",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(40)",
                nullable: false,
                defaultValue: "NotRequired");

            migrationBuilder.AddColumn<string>(
                name: "CustodyMode",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(96)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CustodyNextRetryAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyProvider",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(96)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustodyProviderProfile",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CustodyRetryCount",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "DeletionWindowDays",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionContextHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptionContextVersion",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KmsAccountBoundary",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(160)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KmsAlias",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(256)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KmsDeletionDate",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KmsDeletionScheduledAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KmsKeyArn",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KmsKeyCreatedAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KmsKeyDisabledAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KmsKeyId",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(256)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KmsRegion",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(64)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KmsTagSetHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "KmsTagsVerifiedAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastReconciledAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PublicCustodyReferenceHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(128)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SealedEnvelopeHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                type: "varchar(128)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAdminOnlyProtectedTallyEnvelopeRecord_CustodyMode_C~",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                columns: new[] { "CustodyMode", "CustodyLifecycleState" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAdminOnlyProtectedTallyEnvelopeRecord_ElectionId_Se~",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                columns: new[] { "ElectionId", "SelectedProfileId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAdminOnlyProtectedTallyEnvelopeRecord_KmsAlias",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord",
                column: "KmsAlias");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ElectionAdminOnlyProtectedTallyEnvelopeRecord_CustodyMode_C~",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionAdminOnlyProtectedTallyEnvelopeRecord_ElectionId_Se~",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionAdminOnlyProtectedTallyEnvelopeRecord_KmsAlias",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyActionServiceIdentity",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyExceptionId",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyLastAction",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyLastErrorCode",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyLastErrorMessage",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyLifecycleState",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyMode",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyNextRetryAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyProvider",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyProviderProfile",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "CustodyRetryCount",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "DeletionWindowDays",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "EncryptionContextHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "EncryptionContextVersion",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsAccountBoundary",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsAlias",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsDeletionDate",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsDeletionScheduledAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsKeyArn",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsKeyCreatedAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsKeyDisabledAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsKeyId",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsRegion",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsTagSetHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "KmsTagsVerifiedAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "LastReconciledAt",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "PublicCustodyReferenceHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");

            migrationBuilder.DropColumn(
                name: "SealedEnvelopeHash",
                schema: "Elections",
                table: "ElectionAdminOnlyProtectedTallyEnvelopeRecord");
        }
    }
}
