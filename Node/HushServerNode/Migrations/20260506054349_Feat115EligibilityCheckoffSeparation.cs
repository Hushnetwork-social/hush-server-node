using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat115EligibilityCheckoffSeparation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_LinkedActorPublicAdd~",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_LinkedActorPublicAddre~",
                schema: "Elections",
                table: "ElectionRosterEntryRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_LinkedActor~",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord");

            migrationBuilder.AddColumn<string>(
                name: "ActorLinkMultiplicityPolicy",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: false,
                defaultValue: "SingleRosterEntryPerActor");

            migrationBuilder.AddColumn<string>(
                name: "CheckoffVisibilityPolicy",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: false,
                defaultValue: "RestrictedOwnerAuditor");

            migrationBuilder.AddColumn<string>(
                name: "ContactCodeProviderReadiness",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(40)",
                nullable: false,
                defaultValue: "DevOnly");

            migrationBuilder.AddColumn<string>(
                name: "IdentityLinkPolicy",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: false,
                defaultValue: "ContactCodeV1");

            migrationBuilder.CreateTable(
                name: "ElectionCommitmentSchemeEvidenceRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    DeclaredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    CommitmentSchemeVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    CommitmentSchemeVersionHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    NullifierSchemeVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    NullifierSchemeVersionHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    RosterCanonicalizationVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    RosterCanonicalizationVersionHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    EligibilityPolicyCanonicalizationVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    EligibilityPolicyCanonicalizationVersionHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    DeclaredByActor = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionCommitmentSchemeEvidenceRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionEligibilityPolicyEvidenceRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    EligibilityMutationPolicy = table.Column<string>(type: "varchar(96)", nullable: false),
                    IdentityLinkPolicy = table.Column<string>(type: "varchar(96)", nullable: false),
                    CheckoffVisibilityPolicy = table.Column<string>(type: "varchar(96)", nullable: false),
                    ActorLinkMultiplicityPolicy = table.Column<string>(type: "varchar(96)", nullable: false),
                    ContactCodeProviderReadiness = table.Column<string>(type: "varchar(40)", nullable: false),
                    DeclaredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    EligibilityPolicyId = table.Column<string>(type: "varchar(128)", nullable: false),
                    EligibilityPolicyVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    EligibilityPolicyCanonicalizationVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    EligibilityPolicyCanonicalizationVersionHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    DeclaredByActor = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionEligibilityPolicyEvidenceRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionRosterImportEvidenceRecord",
                schema: "Elections",
                columns: table => new
                {
                    RosterImportId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    RosterImportVersion = table.Column<int>(type: "integer", nullable: false),
                    AcceptedRowCount = table.Column<int>(type: "integer", nullable: false),
                    RejectedRowCount = table.Column<int>(type: "integer", nullable: false),
                    InvalidRowRejectionCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateIdRejectionCount = table.Column<int>(type: "integer", nullable: false),
                    DuplicateContactWarningCount = table.Column<int>(type: "integer", nullable: false),
                    ImportedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    RosterSourceFileHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    RosterCanonicalHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    RosterCanonicalizationVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    RosterCanonicalizationVersionHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    ImportedByActor = table.Column<string>(type: "varchar(160)", nullable: false),
                    RejectedRows = table.Column<string>(type: "jsonb", nullable: false),
                    DuplicateContactWarnings = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionRosterImportEvidenceRecord", x => x.RosterImportId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_LinkedActorPublicAdd~",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_LinkedActorPublicAddre~",
                schema: "Elections",
                table: "ElectionRosterEntryRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_LinkedActor~",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCommitmentSchemeEvidenceRecord_ElectionId_Commitmen~",
                schema: "Elections",
                table: "ElectionCommitmentSchemeEvidenceRecord",
                columns: new[] { "ElectionId", "CommitmentSchemeVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCommitmentSchemeEvidenceRecord_ElectionId_DeclaredAt",
                schema: "Elections",
                table: "ElectionCommitmentSchemeEvidenceRecord",
                columns: new[] { "ElectionId", "DeclaredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEligibilityPolicyEvidenceRecord_ElectionId_Declared~",
                schema: "Elections",
                table: "ElectionEligibilityPolicyEvidenceRecord",
                columns: new[] { "ElectionId", "DeclaredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionEligibilityPolicyEvidenceRecord_ElectionId_Eligibil~",
                schema: "Elections",
                table: "ElectionEligibilityPolicyEvidenceRecord",
                columns: new[] { "ElectionId", "EligibilityPolicyId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterImportEvidenceRecord_ElectionId_ImportedAt",
                schema: "Elections",
                table: "ElectionRosterImportEvidenceRecord",
                columns: new[] { "ElectionId", "ImportedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterImportEvidenceRecord_ElectionId_RosterCanonic~",
                schema: "Elections",
                table: "ElectionRosterImportEvidenceRecord",
                columns: new[] { "ElectionId", "RosterCanonicalHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterImportEvidenceRecord_ElectionId_RosterImportV~",
                schema: "Elections",
                table: "ElectionRosterImportEvidenceRecord",
                columns: new[] { "ElectionId", "RosterImportVersion" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionCommitmentSchemeEvidenceRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionEligibilityPolicyEvidenceRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionRosterImportEvidenceRecord",
                schema: "Elections");

            migrationBuilder.DropIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_LinkedActorPublicAdd~",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_LinkedActorPublicAddre~",
                schema: "Elections",
                table: "ElectionRosterEntryRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_LinkedActor~",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord");

            migrationBuilder.DropColumn(
                name: "ActorLinkMultiplicityPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "CheckoffVisibilityPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "ContactCodeProviderReadiness",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "IdentityLinkPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_LinkedActorPublicAdd~",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_LinkedActorPublicAddre~",
                schema: "Elections",
                table: "ElectionRosterEntryRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_LinkedActor~",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" },
                unique: true);
        }
    }
}
