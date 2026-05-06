using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushNode.Elections.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Feat116Sp06ControlProfileProjection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.AddColumn<byte[]>(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionRecord",
                type: "integer",
                nullable: true);

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
                name: "ControlDomainProfileId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ControlDomainProfileVersion",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(32)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdentityLinkPolicy",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: false,
                defaultValue: "ContactCodeV1");

            migrationBuilder.AddColumn<string>(
                name: "ThresholdProfileId",
                schema: "Elections",
                table: "ElectionRecord",
                type: "varchar(96)",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "varchar(40)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "bytea",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreparedBallotHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "varchar(256)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "varchar(256)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReceiptCommitmentScheme",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                type: "varchar(160)",
                nullable: true);

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
                name: "ElectionPreparedBallotCommitmentRecord",
                schema: "Elections",
                columns: table => new
                {
                    PreparedBallotId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    BallotDefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<string>(type: "varchar(24)", nullable: false),
                    PrecommittedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SpoilMarkerId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcceptedBallotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SpoiledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CastAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    LinkedActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    PreparedBallotHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    BallotDefinitionHash = table.Column<byte[]>(type: "bytea", nullable: false),
                    CeremonyProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofStatementId = table.Column<string>(type: "varchar(160)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPreparedBallotCommitmentRecord", x => x.PreparedBallotId);
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

            migrationBuilder.CreateTable(
                name: "ElectionSpoiledPreparedBallotRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    PreparedBallotId = table.Column<Guid>(type: "uuid", nullable: false),
                    SpoiledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    PreparedBallotHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    SpoiledTranscriptHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    SpoilRecordHash = table.Column<string>(type: "varchar(256)", nullable: false),
                    LocalVerifierVersion = table.Column<string>(type: "varchar(96)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionSpoiledPreparedBallotRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionVoterCeremonyRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    BallotDefinitionVersion = table.Column<int>(type: "integer", nullable: false),
                    PreparedPackageCount = table.Column<int>(type: "integer", nullable: false),
                    SpoiledPackageCount = table.Column<int>(type: "integer", nullable: false),
                    FinalState = table.Column<string>(type: "varchar(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FinalAcceptedBallotId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    OrganizationVoterId = table.Column<string>(type: "varchar(128)", nullable: false),
                    LinkedActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    CeremonyProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    BallotDefinitionHash = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionVoterCeremonyRecord", x => x.Id);
                });

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
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                columns: new[] { "ElectionId", "PreparedBallotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord",
                columns: new[] { "ElectionId", "ReceiptCommitment" },
                unique: true);

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
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_Organizat~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "OrganizationVoterId", "PrecommittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_PreparedB~",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "PreparedBallotHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPreparedBallotCommitmentRecord_ElectionId_State",
                schema: "Elections",
                table: "ElectionPreparedBallotCommitmentRecord",
                columns: new[] { "ElectionId", "State" });

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

            migrationBuilder.CreateIndex(
                name: "IX_ElectionSpoiledPreparedBallotRecord_ElectionId_PreparedBall~",
                schema: "Elections",
                table: "ElectionSpoiledPreparedBallotRecord",
                columns: new[] { "ElectionId", "PreparedBallotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionSpoiledPreparedBallotRecord_ElectionId_SpoiledAt",
                schema: "Elections",
                table: "ElectionSpoiledPreparedBallotRecord",
                columns: new[] { "ElectionId", "SpoiledAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionSpoiledPreparedBallotRecord_ElectionId_SpoilRecordH~",
                schema: "Elections",
                table: "ElectionSpoiledPreparedBallotRecord",
                columns: new[] { "ElectionId", "SpoilRecordHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_FinalState",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "FinalState" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_LinkedActorPublicAdd~",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "LinkedActorPublicAddress" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionVoterCeremonyRecord_ElectionId_OrganizationVoterId",
                schema: "Elections",
                table: "ElectionVoterCeremonyRecord",
                columns: new[] { "ElectionId", "OrganizationVoterId" },
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
                name: "ElectionPreparedBallotCommitmentRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionRosterImportEvidenceRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionSpoiledPreparedBallotRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionVoterCeremonyRecord",
                schema: "Elections");

            migrationBuilder.DropIndex(
                name: "IX_ElectionRosterEntryRecord_ElectionId_LinkedActorPublicAddre~",
                schema: "Elections",
                table: "ElectionRosterEntryRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionCommitmentRegistrationRecord_ElectionId_LinkedActor~",
                schema: "Elections",
                table: "ElectionCommitmentRegistrationRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropIndex(
                name: "IX_ElectionAcceptedBallotRecord_ElectionId_ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "ActorLinkMultiplicityPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionVersion",
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
                name: "ControlDomainProfileId",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "ControlDomainProfileVersion",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "IdentityLinkPolicy",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "ThresholdProfileId",
                schema: "Elections",
                table: "ElectionRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionMutationPolicy",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionSealedAt",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionBoundaryArtifactRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "BallotDefinitionVersion",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "PreparedBallotHash",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "PreparedBallotId",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "ReceiptCommitment",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

            migrationBuilder.DropColumn(
                name: "ReceiptCommitmentScheme",
                schema: "Elections",
                table: "ElectionAcceptedBallotRecord");

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
