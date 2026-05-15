using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HushNode.Elections.Storage.Migrations
{
    /// <inheritdoc />
    public partial class Feat128AnomalyEvidenceRecords : Migration
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
                name: "ElectionAnomalyAttachmentManifestRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AttachmentKindId = table.Column<string>(type: "varchar(96)", nullable: false),
                    EncryptedPayloadReference = table.Column<string>(type: "varchar(160)", nullable: false),
                    EncryptedPayloadHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContentKeyWrapsJson = table.Column<string>(type: "jsonb", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "varchar(128)", nullable: false),
                    ValidationStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    ScannerStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    PayloadAvailabilityStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    ClarificationRequestId = table.Column<Guid>(type: "uuid", nullable: true),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    ActorRoleId = table.Column<string>(type: "varchar(64)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyAttachmentManifestRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionAnomalyEvidenceRedactionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    TargetKindId = table.Column<string>(type: "varchar(96)", nullable: false),
                    TargetId = table.Column<string>(type: "varchar(160)", nullable: false),
                    ReasonCodeId = table.Column<string>(type: "varchar(96)", nullable: false),
                    OriginalHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ReplacementManifestHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    TombstoneStatusId = table.Column<string>(type: "varchar(64)", nullable: true),
                    HoldReference = table.Column<string>(type: "text", nullable: true),
                    ActorPublicAddress = table.Column<string>(type: "varchar(160)", nullable: false),
                    SourceTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SourceBlockHeight = table.Column<long>(type: "bigint", nullable: true),
                    SourceBlockId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyEvidenceRedactionRecord", x => x.Id);
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
                name: "ElectionAnomalyRestrictedPayloadRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    AnomalyThreadId = table.Column<Guid>(type: "uuid", nullable: false),
                    PayloadReference = table.Column<string>(type: "varchar(160)", nullable: false),
                    EncryptedPayload = table.Column<byte[]>(type: "bytea", nullable: false),
                    EncryptedPayloadHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ContentHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "varchar(128)", nullable: false),
                    ScannerStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    PayloadAvailabilityStatusId = table.Column<string>(type: "varchar(64)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastCheckedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionAnomalyRestrictedPayloadRecord", x => x.Id);
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

            migrationBuilder.CreateTable(
                name: "ElectionPublicationProofSessionRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Status = table.Column<string>(type: "varchar(32)", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    ChunkCount = table.Column<int>(type: "integer", nullable: false),
                    RetryCount = table.Column<int>(type: "integer", nullable: false),
                    DeletionReceiptId = table.Column<Guid>(type: "uuid", nullable: true),
                    ProofMode = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofConstruction = table.Column<string>(type: "varchar(128)", nullable: false),
                    StatementId = table.Column<string>(type: "varchar(128)", nullable: false),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true),
                    AcceptedBallotSetHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    PublishedBallotStreamHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    TranscriptHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ProofHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ServerVerifierOutputHash = table.Column<string>(type: "varchar(128)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationProofSessionRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublicationProofTranscriptRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ProofSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    PublishedBallotCount = table.Column<int>(type: "integer", nullable: false),
                    CiphertextSlotCount = table.Column<int>(type: "integer", nullable: false),
                    GeneratedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    TranscriptVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofMode = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofConstruction = table.Column<string>(type: "varchar(128)", nullable: false),
                    StatementId = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProfileId = table.Column<string>(type: "varchar(96)", nullable: false),
                    BallotDefinitionHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    BallotEncryptionSchemeVersion = table.Column<string>(type: "varchar(96)", nullable: false),
                    ElectionPublicKeyId = table.Column<string>(type: "varchar(128)", nullable: false),
                    AcceptedBallotSetHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    PublishedBallotStreamHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofSystemVersion = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofBytes = table.Column<string>(type: "text", nullable: false),
                    ProofHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    TranscriptHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ExternalReviewStatus = table.Column<string>(type: "varchar(64)", nullable: false),
                    GeneratorReleaseHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    VerifierReleaseHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    StatementHashSha512 = table.Column<string>(type: "varchar(128)", nullable: true),
                    FiatShamirTranscriptHashSha512 = table.Column<string>(type: "varchar(128)", nullable: true),
                    CanonicalProofBytesHex = table.Column<string>(type: "text", nullable: true),
                    CanonicalProofHashSha512 = table.Column<string>(type: "varchar(128)", nullable: true),
                    CanonicalProofByteLength = table.Column<int>(type: "integer", nullable: true),
                    PublicPrivacyBoundary = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationProofTranscriptRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublicationWitnessDeletionReceiptRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    ProofSessionId = table.Column<Guid>(type: "uuid", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    WitnessCount = table.Column<int>(type: "integer", nullable: false),
                    DeletionStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    WitnessSetHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    TranscriptHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    DeletionActorRef = table.Column<string>(type: "varchar(160)", nullable: true),
                    FailureCode = table.Column<string>(type: "varchar(128)", nullable: true),
                    FailureReason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationWitnessDeletionReceiptRecord", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ElectionPublicationWitnessRecord",
                schema: "Elections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ElectionId = table.Column<string>(type: "varchar(40)", nullable: false),
                    WitnessSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    AcceptedBallotId = table.Column<Guid>(type: "uuid", nullable: false),
                    PublishedSequence = table.Column<long>(type: "bigint", nullable: true),
                    CustodyStatus = table.Column<string>(type: "varchar(32)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    AcceptedEncryptedBallotHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    PublishedEncryptedBallotHash = table.Column<string>(type: "varchar(128)", nullable: true),
                    ProofMode = table.Column<string>(type: "varchar(96)", nullable: false),
                    ProofConstruction = table.Column<string>(type: "varchar(128)", nullable: false),
                    StatementId = table.Column<string>(type: "varchar(128)", nullable: false),
                    ProofProfileVersion = table.Column<string>(type: "varchar(64)", nullable: false),
                    SealedWitnessMaterial = table.Column<string>(type: "text", nullable: false),
                    SealedWitnessMaterialHash = table.Column<string>(type: "varchar(128)", nullable: false),
                    SealAlgorithm = table.Column<string>(type: "varchar(96)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ElectionPublicationWitnessRecord", x => x.Id);
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
                name: "IX_ElectionAnomalyAttachmentManifestRecord_AnomalyThreadId_Att~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "AnomalyThreadId", "AttachmentKindId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_AnomalyThreadId_Cla~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "AnomalyThreadId", "ClarificationRequestId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_ElectionId_PayloadA~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "ElectionId", "PayloadAvailabilityStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_ElectionId_ScannerS~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                columns: new[] { "ElectionId", "ScannerStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_EncryptedPayloadRef~",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                column: "EncryptedPayloadReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_EventId",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyAttachmentManifestRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionAnomalyAttachmentManifestRecord",
                column: "SourceTransactionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_AnomalyThreadId_Targ~",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
                columns: new[] { "AnomalyThreadId", "TargetKindId", "TargetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_ElectionId_ReasonCod~",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
                columns: new[] { "ElectionId", "ReasonCodeId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_EventId",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
                column: "EventId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyEvidenceRedactionRecord_SourceTransactionId",
                schema: "Elections",
                table: "ElectionAnomalyEvidenceRedactionRecord",
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
                name: "IX_ElectionAnomalyRestrictedPayloadRecord_AnomalyThreadId_Payl~",
                schema: "Elections",
                table: "ElectionAnomalyRestrictedPayloadRecord",
                columns: new[] { "AnomalyThreadId", "PayloadAvailabilityStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRestrictedPayloadRecord_ElectionId_ScannerSt~",
                schema: "Elections",
                table: "ElectionAnomalyRestrictedPayloadRecord",
                columns: new[] { "ElectionId", "ScannerStatusId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionAnomalyRestrictedPayloadRecord_PayloadReference",
                schema: "Elections",
                table: "ElectionAnomalyRestrictedPayloadRecord",
                column: "PayloadReference",
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

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofSessionRecord_ElectionId_StartedAt",
                schema: "Elections",
                table: "ElectionPublicationProofSessionRecord",
                columns: new[] { "ElectionId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofSessionRecord_ElectionId_Status",
                schema: "Elections",
                table: "ElectionPublicationProofSessionRecord",
                columns: new[] { "ElectionId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofSessionRecord_ElectionId_WitnessSet~",
                schema: "Elections",
                table: "ElectionPublicationProofSessionRecord",
                columns: new[] { "ElectionId", "WitnessSetId" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofTranscriptRecord_ElectionId_Generat~",
                schema: "Elections",
                table: "ElectionPublicationProofTranscriptRecord",
                columns: new[] { "ElectionId", "GeneratedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofTranscriptRecord_ElectionId_Transcr~",
                schema: "Elections",
                table: "ElectionPublicationProofTranscriptRecord",
                columns: new[] { "ElectionId", "TranscriptHash" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationProofTranscriptRecord_ProofSessionId",
                schema: "Elections",
                table: "ElectionPublicationProofTranscriptRecord",
                column: "ProofSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessDeletionReceiptRecord_ElectionId_~",
                schema: "Elections",
                table: "ElectionPublicationWitnessDeletionReceiptRecord",
                columns: new[] { "ElectionId", "DeletedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessDeletionReceiptRecord_ProofSessio~",
                schema: "Elections",
                table: "ElectionPublicationWitnessDeletionReceiptRecord",
                column: "ProofSessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessRecord_ElectionId_AcceptedBallotId",
                schema: "Elections",
                table: "ElectionPublicationWitnessRecord",
                columns: new[] { "ElectionId", "AcceptedBallotId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessRecord_ElectionId_CustodyStatus",
                schema: "Elections",
                table: "ElectionPublicationWitnessRecord",
                columns: new[] { "ElectionId", "CustodyStatus" });

            migrationBuilder.CreateIndex(
                name: "IX_ElectionPublicationWitnessRecord_ElectionId_WitnessSetId",
                schema: "Elections",
                table: "ElectionPublicationWitnessRecord",
                columns: new[] { "ElectionId", "WitnessSetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ElectionAnomalyActionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyAttachmentManifestRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyEvidenceRedactionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyMessageEnvelopeRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyRecipientWrapRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyRestrictedPayloadRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyThreadEventRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionAnomalyThreadRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationProofSessionRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationProofTranscriptRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationWitnessDeletionReceiptRecord",
                schema: "Elections");

            migrationBuilder.DropTable(
                name: "ElectionPublicationWitnessRecord",
                schema: "Elections");

            migrationBuilder.DropColumn(
                name: "AnomalySubmissionWindowClosesAt",
                schema: "Elections",
                table: "ElectionRecord");
        }
    }
}
