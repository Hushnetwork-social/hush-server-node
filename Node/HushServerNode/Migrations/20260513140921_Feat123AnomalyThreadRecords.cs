using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushServerNode.Migrations
{
    /// <inheritdoc />
    public partial class Feat123AnomalyThreadRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "AnomalySubmissionWindowClosesAt",
                schema: "Elections",
                table: "ElectionRecord",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyActionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionNonce = table.Column<Guid>(type: "uuid", nullable: true),
                    ActionType = table.Column<string>(type: "varchar(96)", nullable: false),
                    ActionOutcomeId = table.Column<string>(type: "varchar(64)", nullable: false),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ValidationCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    DiagnosticReference = table.Column<string>(type: "text", nullable: true),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyActionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyMessageEnvelopeRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    MessageKindId = table.Column<string>(type: "varchar(96)", nullable: false),
                    EncryptedBody = table.Column<string>(type: "text", nullable: false),
                    EncryptedBodyHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    PlaintextBodyHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    PlaintextCharacterCount = table.Column<int>(type: "integer", nullable: false),
                    EncryptionAlgorithm = table.Column<string>(type: "varchar(96)", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyMessageEnvelopeRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyRecipientWrapRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MessageEnvelopeId = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    RecipientRoleId = table.Column<string>(type: "varchar(64)", nullable: false),
                    RecipientPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    RecipientKeyFingerprint = table.Column<string>(type: "varchar(256)", nullable: false),
                    EncryptedContentKey = table.Column<string>(type: "text", nullable: false),
                    WrapAlgorithm = table.Column<string>(type: "varchar(96)", nullable: false),
                    WrapStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyRecipientWrapRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyThreadEventRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    EventTypeId = table.Column<string>(type: "varchar(96)", nullable: false),
                    EventPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                    EventHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    PreviousEventHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ActionNonce = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyThreadEventRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyThreadRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    SubmitterPersonScopeId = table.Column<string>(type: "varchar(160)", nullable: false),
                    SubmitterPersonScopeDerivationVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    SubmitterActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    SubmitterRoleContextId = table.Column<string>(type: "varchar(64)", nullable: true),
                    SubmitterRoleEvidenceTypeId = table.Column<string>(type: "varchar(96)", nullable: false),
                    SubmitterRoleEvidenceReference = table.Column<string>(type: "text", nullable: false),
                    LifecycleStateAtSubmission = table.Column<string>(type: "varchar(32)", nullable: false),
                    SubmissionWindowClosesAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CurrentCategoryId = table.Column<string>(type: "varchar(96)", nullable: false),
                    CurrentCaseStateId = table.Column<string>(type: "varchar(64)", nullable: false),
                    SeverityCandidateId = table.Column<string>(type: "varchar(64)", nullable: true),
                    GovernedDecisionRef = table.Column<string>(type: "text", nullable: true),
                    HasOpenClarificationRequest = table.Column<bool>(type: "boolean", nullable: false),
                    OpenClarificationRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentThreadHash = table.Column<string>(type: "varchar(128)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyThreadRecord", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyActionRecord_AnomalyThreadId_RecordedAt",
                schema: "Elections",
                table: "ElectionAnomalyActionRecord",
                columns: new[] { "AnomalyThreadId", "RecordedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyActionRecord_ElectionId_ActionOutcomeId",
                schema: "Elections",
                table: "ElectionAnomalyActionRecord",
                columns: new[] { "ElectionId", "ActionOutcomeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyActionRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionAnomalyActionRecord",
                column: "SourceTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyMessageEnvelopeRecord_AnomalyThreadId_Messag~",
                schema: "Elections",
                table: "ElectionAnomalyMessageEnvelopeRecord",
                columns: new[] { "AnomalyThreadId", "MessageKindId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyMessageEnvelopeRecord_EncryptedBodyHash",
                schema: "Elections",
                table: "ElectionAnomalyMessageEnvelopeRecord",
                column: "EncryptedBodyHash");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyMessageEnvelopeRecord_EventId",
                schema: "Elections",
                table: "ElectionAnomalyMessageEnvelopeRecord",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRecipientWrapRecord_AnomalyThreadId_Recipien~",
                schema: "Elections",
                table: "ElectionAnomalyRecipientWrapRecord",
                columns: new[] { "AnomalyThreadId", "RecipientRoleId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRecipientWrapRecord_ElectionId_WrapStatusId",
                schema: "Elections",
                table: "ElectionAnomalyRecipientWrapRecord",
                columns: new[] { "ElectionId", "WrapStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRecipientWrapRecord_MessageEnvelopeId_Recipi~",
                schema: "Elections",
                table: "ElectionAnomalyRecipientWrapRecord",
                columns: new[] { "MessageEnvelopeId", "RecipientRoleId", "RecipientPublicAddress" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadEventRecord_AnomalyThreadId_Sequence",
                schema: "Elections",
                table: "ElectionAnomalyThreadEventRecord",
                columns: new[] { "AnomalyThreadId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadEventRecord_ElectionId_EventTypeId",
                schema: "Elections",
                table: "ElectionAnomalyThreadEventRecord",
                columns: new[] { "ElectionId", "EventTypeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadEventRecord_EventHash",
                schema: "Elections",
                table: "ElectionAnomalyThreadEventRecord",
                column: "EventHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadEventRecord_SourceTransactionId_Action~",
                schema: "Elections",
                table: "ElectionAnomalyThreadEventRecord",
                columns: new[] { "SourceTransactionId", "ActionNonce" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadRecord_ElectionId_CurrentCaseStateId",
                schema: "Elections",
                table: "ElectionAnomalyThreadRecord",
                columns: new[] { "ElectionId", "CurrentCaseStateId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadRecord_ElectionId_CurrentCategoryId",
                schema: "Elections",
                table: "ElectionAnomalyThreadRecord",
                columns: new[] { "ElectionId", "CurrentCategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadRecord_ElectionId_SubmitterPersonScope~",
                schema: "Elections",
                table: "ElectionAnomalyThreadRecord",
                columns: new[] { "ElectionId", "SubmitterPersonScopeId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadRecord_LastUpdatedAt",
                schema: "Elections",
                table: "ElectionAnomalyThreadRecord",
                column: "LastUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyThreadRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionAnomalyThreadRecord",
                column: "SourceTransactionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionAnomalyActionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyMessageEnvelopeRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyRecipientWrapRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyThreadEventRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyThreadRecord",
                schema: "Elections");

            migrationBuilder.DropColumn(
                name: "AnomalySubmissionWindowClosesAt",
                schema: "Elections",
                table: "ElectionRecord");
        }
    }
}
