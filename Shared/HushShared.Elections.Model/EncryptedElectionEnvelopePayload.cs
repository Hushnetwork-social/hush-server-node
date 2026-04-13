using System.Text.Json;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;

namespace HushShared.Elections.Model;

public record EncryptedElectionEnvelopePayload(
    ElectionId ElectionId,
    string EnvelopeVersion,
    string NodeEncryptedElectionPrivateKey,
    string ActorEncryptedElectionPrivateKey,
    string EncryptedPayload) : ITransactionPayloadKind;

public record EncryptedElectionActionEnvelope(
    string ActionType,
    JsonElement ActionPayload);

public record CreateElectionDraftActionPayload(
    string OwnerPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft);

public record UpdateElectionDraftActionPayload(
    string ActorPublicAddress,
    string SnapshotReason,
    ElectionDraftSpecification Draft);

public record ImportElectionRosterActionPayload(
    string ActorPublicAddress,
    IReadOnlyList<ElectionRosterImportItem> RosterEntries);

public record ClaimElectionRosterEntryActionPayload(
    string ActorPublicAddress,
    string OrganizationVoterId,
    string VerificationCode);

public record ActivateElectionRosterEntryActionPayload(
    string ActorPublicAddress,
    string OrganizationVoterId);

public record RegisterElectionVotingCommitmentActionPayload(
    string ActorPublicAddress,
    string CommitmentHash);

public record AcceptElectionBallotCastActionPayload(
    string ActorPublicAddress,
    string IdempotencyKey,
    string EncryptedBallotPackage,
    string ProofBundle,
    string BallotNullifier,
    Guid OpenArtifactId,
    byte[] EligibleSetHash,
    Guid CeremonyVersionId,
    string DkgProfileId,
    string TallyPublicKeyFingerprint);

public record InviteElectionTrusteeActionPayload(
    Guid InvitationId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string TrusteeEncryptedElectionPrivateKey,
    string? TrusteeDisplayName);

public record CreateElectionReportAccessGrantActionPayload(
    string ActorPublicAddress,
    string DesignatedAuditorPublicAddress);

public record ResolveElectionTrusteeInvitationActionPayload(
    Guid InvitationId,
    string ActorPublicAddress);

public record RevokeElectionTrusteeInvitationActionPayload(
    Guid InvitationId,
    string ActorPublicAddress);

public record StartElectionGovernedProposalActionPayload(
    Guid ProposalId,
    ElectionGovernedActionType ActionType,
    string ActorPublicAddress);

public record ApproveElectionGovernedProposalActionPayload(
    Guid ProposalId,
    string ActorPublicAddress,
    string? ApprovalNote);

public record RetryElectionGovernedProposalExecutionActionPayload(
    Guid ProposalId,
    string ActorPublicAddress);

public record OpenElectionActionPayload(
    string ActorPublicAddress,
    ElectionWarningCode[] RequiredWarningCodes,
    byte[]? FrozenEligibleVoterSetHash,
    string? TrusteePolicyExecutionReference,
    string? ReportingPolicyExecutionReference,
    string? ReviewWindowExecutionReference);

public record CloseElectionActionPayload(
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash,
    byte[]? FinalEncryptedTallyHash);

public record FinalizeElectionActionPayload(
    string ActorPublicAddress,
    byte[]? AcceptedBallotSetHash,
    byte[]? FinalEncryptedTallyHash);

public record SubmitElectionFinalizationShareActionPayload(
    Guid FinalizationSessionId,
    string ActorPublicAddress,
    int ShareIndex,
    string ShareVersion,
    ElectionFinalizationTargetType TargetType,
    Guid ClaimedCloseArtifactId,
    byte[]? ClaimedAcceptedBallotSetHash,
    byte[]? ClaimedFinalEncryptedTallyHash,
    string ClaimedTargetTallyId,
    Guid? ClaimedCeremonyVersionId,
    string? ClaimedTallyPublicKeyFingerprint,
    string ShareMaterial);

public record StartElectionCeremonyActionPayload(
    string ActorPublicAddress,
    string ProfileId);

public record RestartElectionCeremonyActionPayload(
    string ActorPublicAddress,
    string ProfileId,
    string RestartReason);

public record PublishElectionCeremonyTransportKeyActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string TransportPublicKeyFingerprint);

public record JoinElectionCeremonyActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress);

public record RecordElectionCeremonySelfTestActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress);

public record SubmitElectionCeremonyMaterialActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string? RecipientTrusteeUserAddress,
    string MessageType,
    string PayloadVersion,
    string EncryptedPayload,
    string PayloadFingerprint,
    string ShareVersion);

public record RecordElectionCeremonyValidationFailureActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string ValidationFailureReason,
    string? EvidenceReference);

public record CompleteElectionCeremonyTrusteeActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string TrusteeUserAddress,
    string ShareVersion,
    string? TallyPublicKeyFingerprint);

public record RecordElectionCeremonyShareExportActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    string ShareVersion);

public record RecordElectionCeremonyShareImportActionPayload(
    Guid CeremonyVersionId,
    string ActorPublicAddress,
    ElectionId ImportedElectionId,
    Guid ImportedCeremonyVersionId,
    string ImportedTrusteeUserAddress,
    string ImportedShareVersion);

public static class EncryptedElectionEnvelopeActionTypes
{
    public const string CreateDraft = "create_draft";
    public const string UpdateDraft = "update_draft";
    public const string ImportRoster = "import_roster";
    public const string ClaimRosterEntry = "claim_roster_entry";
    public const string ActivateRosterEntry = "activate_roster_entry";
    public const string RegisterVotingCommitment = "register_voting_commitment";
    public const string AcceptBallotCast = "accept_ballot_cast";
    public const string InviteTrustee = "invite_trustee";
    public const string CreateReportAccessGrant = "create_report_access_grant";
    public const string AcceptTrusteeInvitation = "accept_trustee_invitation";
    public const string RejectTrusteeInvitation = "reject_trustee_invitation";
    public const string RevokeTrusteeInvitation = "revoke_trustee_invitation";
    public const string StartGovernedProposal = "start_governed_proposal";
    public const string ApproveGovernedProposal = "approve_governed_proposal";
    public const string RetryGovernedProposalExecution = "retry_governed_proposal_execution";
    public const string OpenElection = "open_election";
    public const string CloseElection = "close_election";
    public const string FinalizeElection = "finalize_election";
    public const string SubmitFinalizationShare = "submit_finalization_share";
    public const string StartCeremony = "start_ceremony";
    public const string RestartCeremony = "restart_ceremony";
    public const string PublishCeremonyTransportKey = "publish_ceremony_transport_key";
    public const string JoinCeremony = "join_ceremony";
    public const string RecordCeremonySelfTestSuccess = "record_ceremony_self_test_success";
    public const string SubmitCeremonyMaterial = "submit_ceremony_material";
    public const string RecordCeremonyValidationFailure = "record_ceremony_validation_failure";
    public const string CompleteCeremonyTrustee = "complete_ceremony_trustee";
    public const string RecordCeremonyShareExport = "record_ceremony_share_export";
    public const string RecordCeremonyShareImport = "record_ceremony_share_import";
}

public static class EncryptedElectionEnvelopePayloadHandler
{
    public static Guid EncryptedElectionEnvelopePayloadKind { get; } =
        Guid.Parse("e839953b-dc29-4d81-a44e-9694f6614943");

    public const string CurrentEnvelopeVersion = "election-envelope-v1";

    public static UnsignedTransaction<EncryptedElectionEnvelopePayload> CreateNew(
        ElectionId electionId,
        string envelopeVersion,
        string nodeEncryptedElectionPrivateKey,
        string actorEncryptedElectionPrivateKey,
        string encryptedPayload) =>
        UnsignedTransactionHandler.CreateNew(
            EncryptedElectionEnvelopePayloadKind,
            Timestamp.Current,
            new EncryptedElectionEnvelopePayload(
                electionId,
                envelopeVersion,
                nodeEncryptedElectionPrivateKey,
                actorEncryptedElectionPrivateKey,
                encryptedPayload));
}
