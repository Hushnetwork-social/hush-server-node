using System;
using System.Collections.Generic;
using System.Linq;

namespace HushShared.Elections.Model;

public static class ElectionAnomalyLimits
{
    public const int InitialBodyMaxCharacters = 1000;
    public const int ClarificationBodyMaxCharacters = 1000;
}

public static class ElectionAnomalyCategoryIds
{
    public const string AccessOrAuthenticationAnomaly = "access_or_authentication_anomaly";
    public const string BallotCastingOrReceiptAnomaly = "ballot_casting_or_receipt_anomaly";
    public const string TrusteeContinuityAnomaly = "trustee_continuity_anomaly";
    public const string CountingOrTallyAnomaly = "counting_or_tally_anomaly";
    public const string ReportingOrAuditPackageAnomaly = "reporting_or_audit_package_anomaly";
    public const string SecurityOrIntegrityConcern = "security_or_integrity_concern";
    public const string ExternalObjectionOrComplaint = "external_objection_or_complaint";
    public const string OtherProcessAnomaly = "other_process_anomaly";

    public static IReadOnlyList<string> All { get; } =
    [
        AccessOrAuthenticationAnomaly,
        BallotCastingOrReceiptAnomaly,
        TrusteeContinuityAnomaly,
        CountingOrTallyAnomaly,
        ReportingOrAuditPackageAnomaly,
        SecurityOrIntegrityConcern,
        ExternalObjectionOrComplaint,
        OtherProcessAnomaly,
    ];

    public static bool IsKnown(string? categoryId) =>
        !string.IsNullOrWhiteSpace(categoryId) &&
        All.Contains(categoryId, StringComparer.Ordinal);
}

public static class ElectionAnomalyCaseStateIds
{
    public const string Submitted = "submitted";
    public const string UnderReview = "under_review";
    public const string AuthorityRequestedInformation = "authority_requested_information";
    public const string SubmitterInformationProvided = "submitter_information_provided";
    public const string OwnerResponded = "owner_responded";
    public const string EscalatedToGovernedDecision = "escalated_to_governed_decision";
    public const string ResolvedNonBlocking = "resolved_non_blocking";
    public const string ClosedDuplicateFollowup = "closed_duplicate_followup";
    public const string ClosedNoFurtherSubmitterInput = "closed_no_further_submitter_input";

    public static IReadOnlyList<string> All { get; } =
    [
        Submitted,
        UnderReview,
        AuthorityRequestedInformation,
        SubmitterInformationProvided,
        OwnerResponded,
        EscalatedToGovernedDecision,
        ResolvedNonBlocking,
        ClosedDuplicateFollowup,
        ClosedNoFurtherSubmitterInput,
    ];

    public static bool IsKnown(string? caseStateId) =>
        !string.IsNullOrWhiteSpace(caseStateId) &&
        All.Contains(caseStateId, StringComparer.Ordinal);
}

public static class ElectionAnomalyValidationCodes
{
    public const string DirectWriteForbidden = "anomaly_direct_write_forbidden";
    public const string InvalidActionSignatory = "anomaly_invalid_action_signatory";
    public const string SubmitterScopeClientSupplied = "anomaly_submitter_scope_client_supplied";
    public const string PersonScopeUnresolved = "anomaly_person_scope_unresolved";
    public const string DuplicateThread = "anomaly_duplicate_thread";
    public const string CategoryInvalid = "anomaly_category_invalid";
    public const string BodyRequired = "anomaly_body_required";
    public const string BodyTooLong = "anomaly_body_too_long";
    public const string SubmissionWindowClosed = "anomaly_submission_window_closed";
    public const string FollowupNotRequested = "anomaly_followup_not_requested";
    public const string ClarificationRequestNotOpen = "anomaly_clarification_request_not_open";
    public const string ClarificationRequestAlreadyOpen = "anomaly_clarification_request_already_open";
    public const string RecipientWrapMissing = "anomaly_recipient_wrap_missing";
    public const string ReadForbidden = "anomaly_read_forbidden";

    public static IReadOnlyList<string> All { get; } =
    [
        DirectWriteForbidden,
        InvalidActionSignatory,
        SubmitterScopeClientSupplied,
        PersonScopeUnresolved,
        DuplicateThread,
        CategoryInvalid,
        BodyRequired,
        BodyTooLong,
        SubmissionWindowClosed,
        FollowupNotRequested,
        ClarificationRequestNotOpen,
        ClarificationRequestAlreadyOpen,
        RecipientWrapMissing,
        ReadForbidden,
    ];

    public static bool IsKnown(string? validationCode) =>
        !string.IsNullOrWhiteSpace(validationCode) &&
        All.Contains(validationCode, StringComparer.Ordinal);
}

public static class ElectionAnomalyMessageKindIds
{
    public const string InitialSubmission = "initial_submission";
    public const string AuthorityInformationRequest = "authority_information_request";
    public const string SubmitterInformationResponse = "submitter_information_response";
    public const string AuthorityResponse = "authority_response";
}

public static class ElectionAnomalyActorRoleContextIds
{
    public const string Voter = "voter";
    public const string Trustee = "trustee";
    public const string DesignatedAuditor = "designated_auditor";
    public const string ElectionOwner = "election_owner";
    public const string AuthorityOperator = "authority_operator";
    public const string ExternalClaimantRegistrar = "external_claimant_registrar";
}

public static class ElectionAnomalyPersonScopeDerivationVersions
{
    public const string V1 = "hush-election-anomaly-person-scope-v1";
    public const string Current = V1;
}

public static class ElectionAnomalyRoleEvidenceTypeIds
{
    public const string ElectionOwner = "election_owner";
    public const string VoterRosterLink = "voter_roster_link";
    public const string TrusteeInvitation = "trustee_invitation";
    public const string DesignatedAuditorGrant = "designated_auditor_grant";
    public const string ExternalClaimantBridge = "external_claimant_bridge";
}

public static class ElectionAnomalyRecipientRoleIds
{
    public const string Submitter = "submitter";
    public const string ElectionOwner = "election_owner";
    public const string DelegatedAuthority = "delegated_authority";
    public const string DesignatedAuditor = "designated_auditor";
}

public static class ElectionAnomalyRecipientWrapStatusIds
{
    public const string Available = "available";
    public const string Missing = "missing";
    public const string PendingBackfill = "pending_backfill";
    public const string NotApplicable = "not_applicable";
}

public static class ElectionAnomalyAttachmentKindIds
{
    public const string SubmitterEvidence = "submitter_evidence";
    public const string AuthorityRequestedEvidence = "authority_requested_evidence";
    public const string AuthorityEvidence = "authority_evidence";
    public const string RestrictedOperationalEvidence = "restricted_operational_evidence";
}

public static class ElectionAnomalyAttachmentValidationStatusIds
{
    public const string ManifestOnly = "manifest_only";
    public const string PendingScan = "pending_scan";
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
}

public static class ElectionAnomalyEventTypeIds
{
    public const string ThreadSubmitted = "thread_submitted";
    public const string AuthorityInformationRequested = "authority_information_requested";
    public const string SubmitterInformationProvided = "submitter_information_provided";
    public const string AuthorityResponded = "authority_responded";
    public const string ThreadClassified = "thread_classified";
    public const string ExternalClaimantRegistered = "external_claimant_registered";
    public const string AttachmentManifestRecorded = "attachment_manifest_recorded";
}

public static class ElectionAnomalyActionOutcomeIds
{
    public const string Accepted = "accepted";
    public const string Rejected = "rejected";
    public const string IgnoredDuplicate = "ignored_duplicate";
}

public record ElectionAnomalyRecipientWrapProjection(
    string RecipientRoleId,
    string WrapStatusId,
    string? RecipientPublicAddress = null,
    string? RecipientKeyFingerprint = null,
    string? EncryptedContentKey = null,
    string? WrapAlgorithm = null);

public record ElectionAnomalyEncryptedMessageProjection(
    Guid MessageId,
    string MessageKindId,
    DateTime RecordedAtUtc,
    string EncryptedBody,
    string EncryptedBodyHash,
    int PlaintextCharacterCount,
    IReadOnlyList<ElectionAnomalyRecipientWrapProjection> RecipientWraps,
    Guid? ClarificationRequestId = null,
    string? AttachmentManifestHash = null);

public record ElectionAnomalyRestrictedRecipientWrapProjection(
    string RecipientRoleId,
    string WrapStatusId);

public record ElectionAnomalyRestrictedMessageProjection(
    Guid MessageId,
    string MessageKindId,
    string EncryptedBody,
    string EncryptedBodyHash,
    int PlaintextCharacterCount,
    IReadOnlyList<ElectionAnomalyRestrictedRecipientWrapProjection> RecipientWraps,
    Guid? ClarificationRequestId = null,
    string? AttachmentManifestHash = null);

public record ElectionAnomalyOwnThreadProjection(
    Guid AnomalyThreadId,
    ElectionId ElectionId,
    string CategoryId,
    string CaseStateId,
    string CurrentThreadHash,
    string? SeverityCandidateId,
    string? GovernedDecisionRef,
    bool HasOpenClarificationRequest,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ElectionAnomalyEncryptedMessageProjection> Messages);

public record ElectionAnomalyOwnerTriageProjection(
    Guid AnomalyThreadId,
    ElectionId ElectionId,
    string CategoryId,
    string CaseStateId,
    string CurrentThreadHash,
    string? SeverityCandidateId,
    string? GovernedDecisionRef,
    string? SubmitterActorPublicAddress,
    string? SubmitterRoleContextId,
    bool HasOpenClarificationRequest,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ElectionAnomalyEncryptedMessageProjection> Messages);

public record ElectionAnomalyCategoryCountProjection(
    string CategoryId,
    int Count);

public record ElectionAnomalyCaseStateCountProjection(
    string CaseStateId,
    int Count);

public record ElectionAnomalyTrusteeCountsProjection(
    ElectionId ElectionId,
    int TotalThreadCount,
    IReadOnlyList<ElectionAnomalyCategoryCountProjection> CategoryCounts,
    IReadOnlyList<ElectionAnomalyCaseStateCountProjection> CaseStateCounts);

public record ElectionAnomalyAuditorRestrictedThreadProjection(
    Guid AnomalyThreadId,
    ElectionId ElectionId,
    string CategoryId,
    string CaseStateId,
    string CurrentThreadHash,
    string? SeverityCandidateId,
    string? GovernedDecisionRef,
    bool HasOpenClarificationRequest,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ElectionAnomalyRestrictedMessageProjection> Messages);

public record ElectionAnomalyAuditorRestrictedReviewProjection(
    ElectionId ElectionId,
    IReadOnlyList<ElectionAnomalyAuditorRestrictedThreadProjection> Threads);

public record ElectionAnomalyReportManifestThreadProjection(
    Guid AnomalyThreadId,
    ElectionId ElectionId,
    string CategoryId,
    string CaseStateId,
    string CurrentThreadHash,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    IReadOnlyList<ElectionAnomalyRestrictedRecipientWrapProjection> RecipientWraps);

public record ElectionAnomalyReportManifestSeedProjection(
    ElectionId ElectionId,
    int TotalThreadCount,
    IReadOnlyList<ElectionAnomalyCategoryCountProjection> CategoryCounts,
    IReadOnlyList<ElectionAnomalyCaseStateCountProjection> CaseStateCounts,
    IReadOnlyList<ElectionAnomalyReportManifestThreadProjection> Threads);
