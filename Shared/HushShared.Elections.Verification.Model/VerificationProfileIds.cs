namespace HushShared.Elections.Verification.Model;

public static class VerificationProfileIds
{
    public const string DevelopmentCurrentV1 = "development_current_v1";
    public const string PublicAnonymousV1 = "public_anonymous_v1";
    public const string RestrictedOwnerAuditorV1 = "restricted_owner_auditor_v1";
    public const string HighAssuranceV1 = "high_assurance_v1";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [
            DevelopmentCurrentV1,
            PublicAnonymousV1,
            RestrictedOwnerAuditorV1,
            HighAssuranceV1,
        ],
        StringComparer.Ordinal);
}

public static class VerificationResultCodes
{
    public const string PackageStructureValid = "package_structure_valid";
    public const string PackageManifestValid = "package_manifest_valid";
    public const string PackageManifestMissingArtifact = "package_manifest_missing_artifact";
    public const string PackageManifestArtifactHashMismatch = "package_manifest_artifact_hash_mismatch";
    public const string PackageUnreadable = "package_unreadable";
    public const string PackageUnparseable = "package_unparseable";
    public const string VerifierProfilePackageMismatch = "verifier_profile_package_mismatch";
    public const string ElectionIdMismatch = "election_id_mismatch";
    public const string ElectionNotFinalized = "election_not_finalized";
    public const string ProtocolPackageDraftPrivate = "protocol_package_draft_private";
    public const string AcceptedBallotInventoryHashMismatch = "accepted_ballot_inventory_hash_mismatch";
    public const string AcceptedBallotDuplicateNullifier = "accepted_ballot_duplicate_nullifier";
    public const string PublishedBallotStreamHashMismatch = "published_ballot_stream_hash_mismatch";
    public const string PublishedBallotSequenceInvalid = "published_ballot_sequence_invalid";
    public const string PublicRestrictedFieldLeak = "public_restricted_field_leak";
    public const string RestrictedEvidenceMissing = "restricted_evidence_missing";
    public const string RestrictedExportUnauthorized = "restricted_export_unauthorized";
    public const string UnsupportedLiveDependency = "unsupported_live_dependency";
    public const string FutureEvidencePending = "future_evidence_pending";
    public const string PublicationProofEvidencePending = "publication_proof_evidence_pending";
    public const string ReleaseIntegrityEvidencePending = "release_integrity_evidence_pending";
    public const string ReleaseIntegrityEvidenceValid = "release_integrity_evidence_valid";
    public const string ReleaseIntegrityManifestMissing = "release_integrity_manifest_missing";
    public const string ReleaseIntegrityEvidenceModeNotAllowed = "release_integrity_evidence_mode_not_allowed";
    public const string ReleaseIntegrityMutableArtifactReference = "release_integrity_mutable_artifact_reference";
    public const string ReleaseIntegrityComponentHashMismatch = "release_integrity_component_hash_mismatch";
    public const string ReleaseIntegrityCircuitOrPackageHashMismatch = "release_integrity_circuit_or_package_hash_mismatch";
    public const string ReleaseIntegrityLifecycleMismatch = "release_integrity_lifecycle_mismatch";
    public const string ReleaseIntegrityMobileEvidenceIncomplete = "release_integrity_mobile_evidence_incomplete";
    public const string ReleaseIntegrityPackageManifestMissingFiles = "release_integrity_package_manifest_missing_files";
    public const string ExternalReviewStatusValid = "external_review_status_valid";
    public const string ExternalReviewProgramMissing = "external_review_program_missing";
    public const string ExternalReviewNotComplete = "external_review_not_complete";
    public const string ExternalReviewScopeMismatch = "external_review_scope_mismatch";
    public const string ExternalReviewReportHashMismatch = "external_review_report_hash_mismatch";
    public const string ExternalReviewOpenFindingsBlockClaims = "external_review_open_findings_block_claims";
    public const string ExternalReviewClaimNotAllowed = "external_review_claim_not_allowed";
    public const string ExternalReviewPublicBoundaryViolation = "external_review_public_boundary_violation";
    public const string ExternalReviewRequiresRedesign = "external_review_requires_redesign";
    public const string OperationalSecurityEvidenceValid = "operational_security_evidence_valid";
    public const string OperationalSecurityEvidenceMissing = "operational_security_evidence_missing";
    public const string OperationalSecurityDevelopmentPlaceholder = "operational_security_development_placeholder";
    public const string OperationalSecurityProfileDeclared = "operational_security_profile_declared";
    public const string OperationalSecurityExceptionDeclared = "operational_security_exception_declared";
    public const string OperationalSecurityBlocked = "operational_security_blocked";
    public const string OperationalSecurityReleaseBindingMissing = "operational_security_release_binding_missing";
    public const string OperationalSecurityAccessSnapshotMissing = "operational_security_access_snapshot_missing";
    public const string OperationalSecurityCustodyModeMissing = "operational_security_custody_mode_missing";
    public const string OperationalSecurityExecutorKeyLifecycleMissing = "operational_security_executor_key_lifecycle_missing";
    public const string OperationalSecurityForbiddenMaterial = "operational_security_forbidden_material";
    public const string OperationalSecurityBackupRestoreMissing = "operational_security_backup_restore_missing";
    public const string OperationalSecurityIncidentDeclarationMissing = "operational_security_incident_declaration_missing";
    public const string OperationalSecurityAuditorRoomMissing = "operational_security_auditor_room_missing";
    public const string RegulatoryClaimShapeValid = "regulatory_claim_shape_valid";
    public const string RegulatoryClaimAllowedByRegister = "regulatory_claim_allowed_by_register";
    public const string RegulatoryClaimBlockedCertification = "regulatory_claim_blocked_certification";
    public const string RegulatoryTrackerStale = "regulatory_tracker_stale";
    public const string RegulatoryRestrictedWorkpaperBoundary = "regulatory_restricted_workpaper_boundary";
    public const string RegulatoryClaimNotLegalApproval = "regulatory_claim_not_legal_approval";
    public const string ChallengeSpoilEvidencePending = "challenge_spoil_evidence_pending";
    public const string ChallengeSpoilEvidenceValid = "challenge_spoil_evidence_valid";
    public const string ChallengeSpoilBallotDefinitionMismatch = "challenge_spoil_ballot_definition_mismatch";
    public const string ChallengeSpoilCountMismatch = "challenge_spoil_count_mismatch";
    public const string ChallengeSpoilReceiptMismatch = "challenge_spoil_receipt_mismatch";
    public const string ChallengeSpoilRestrictedEvidenceMismatch = "challenge_spoil_restricted_evidence_mismatch";
    public const string EligibilityEvidenceValid = "eligibility_evidence_valid";
    public const string EligibilitySchemaInvalid = "eligibility_schema_invalid";
    public const string EligibilityPolicyMissing = "eligibility_policy_missing";
    public const string EligibilityRosterHashMismatch = "eligibility_roster_hash_mismatch";
    public const string EligibilityOpenFreezeViolation = "eligibility_open_freeze_violation";
    public const string EligibilityLateActivationPolicyViolation = "eligibility_late_activation_policy_violation";
    public const string EligibilityLinkEvidenceMissing = "eligibility_link_evidence_missing";
    public const string EligibilityCommitmentInvalid = "eligibility_commitment_invalid";
    public const string EligibilityCommitmentConsumedRight = "eligibility_commitment_consumed_right";
    public const string EligibilityConsumptionWithoutAcceptedCast = "eligibility_consumption_without_accepted_cast";
    public const string EligibilityFailedCastConsumedRight = "eligibility_failed_cast_consumed_right";
    public const string EligibilityCountReconciliationMismatch = "eligibility_count_reconciliation_mismatch";
    public const string EligibilityPublicPrivacyBoundaryViolation = "eligibility_public_privacy_boundary_violation";
    public const string EligibilityBallotPrivacyBoundaryViolation = "eligibility_ballot_privacy_boundary_violation";
    public const string EligibilityDevOnlyVerificationBlocked = "eligibility_dev_only_verification_blocked";
    public const string TrusteeControlDomainEvidenceValid = "trustee_control_domain_evidence_valid";
    public const string TrusteeControlProfileMissing = "trustee_control_profile_missing";
    public const string TrusteeThresholdProfileMismatch = "trustee_threshold_profile_mismatch";
    public const string TrusteeAcceptanceIncomplete = "trustee_acceptance_incomplete";
    public const string TrusteeDuplicateAccount = "trustee_duplicate_account";
    public const string TrusteeDuplicatePerson = "trustee_duplicate_person";
    public const string TrusteeDuplicateCustodyDomain = "trustee_duplicate_custody_domain";
    public const string TrusteeAdminDomainThresholdViolation = "trustee_admin_domain_threshold_violation";
    public const string TrusteeCustodyModeUnsupported = "trustee_custody_mode_unsupported";
    public const string TrusteeReleaseWrongTarget = "trustee_release_wrong_target";
    public const string TrusteeReleaseThresholdNotMet = "trustee_release_threshold_not_met";
    public const string TrusteeRawMaterialLeaked = "trustee_raw_material_leaked";
    public const string TrusteeExceptionPolicyViolation = "trustee_exception_policy_violation";
    public const string PublicationProofEvidenceValid = "publication_proof_evidence_valid";
    public const string PublicationProofTranscriptMissing = "publication_proof_transcript_missing";
    public const string PublicationProofTranscriptInvalid = "publication_proof_transcript_invalid";
    public const string PublicationProofTranscriptHashMismatch = "publication_proof_transcript_hash_mismatch";
    public const string PublicationProofAcceptedSetMismatch = "publication_proof_accepted_set_mismatch";
    public const string PublicationProofPublishedStreamMismatch = "publication_proof_published_stream_mismatch";
    public const string PublicationProofCountMismatch = "publication_proof_count_mismatch";
    public const string PublicationProofPublicKeyMismatch = "publication_proof_public_key_mismatch";
    public const string PublicationProofVerificationFailed = "publication_proof_verification_failed";
    public const string PublicationProofTallyReplayMismatch = "publication_proof_tally_replay_mismatch";
    public const string PublicationProofForbiddenFieldLeak = "publication_proof_forbidden_field_leak";
    public const string PublicationProofWitnessDeletionMissing = "publication_proof_witness_deletion_missing";
    public const string PublicationProofWitnessDeletionInvalid = "publication_proof_witness_deletion_invalid";
    public const string PublicationProofExternalReviewPending = "publication_proof_external_review_pending";
    public const string PublicationProofEnvelopeExceeded = "publication_proof_envelope_exceeded";
}

public static class VerificationExitCodes
{
    public const int Pass = 0;
    public const int Fail = 1;
    public const int UnreadableOrUnparseable = 2;
    public const int ProfileOrPackageMismatch = 3;
    public const int InternalVerifierError = 4;

    public static int FromOverallStatus(VerificationOverallStatus status) =>
        status switch
        {
            VerificationOverallStatus.Pass => Pass,
            VerificationOverallStatus.Warn => Pass,
            VerificationOverallStatus.Fail => Fail,
            VerificationOverallStatus.NotAvailable => UnreadableOrUnparseable,
            _ => InternalVerifierError,
        };
}
