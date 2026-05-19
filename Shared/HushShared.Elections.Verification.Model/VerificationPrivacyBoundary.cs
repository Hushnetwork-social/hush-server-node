using System.Text.RegularExpressions;

namespace HushShared.Elections.Verification.Model;

public static class VerificationPrivacyBoundary
{
    private static readonly RegexOptions ForbiddenValueRegexOptions =
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;

    private static readonly (string Marker, Regex Pattern)[] PublicPackageForbiddenValuePatterns =
    [
        ("aws_access_key_id", new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", ForbiddenValueRegexOptions)),
        ("aws_secret_access_key", new Regex(@"\bAWS_SECRET_ACCESS_KEY\b|\baws_secret_access_key\b", ForbiddenValueRegexOptions)),
        ("aws_session_token", new Regex(@"\bAWS_SESSION_TOKEN\b|\baws_session_token\b", ForbiddenValueRegexOptions)),
        ("private_key_pem", new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", ForbiddenValueRegexOptions)),
        ("kms_key_arn", new Regex(@"\barn:aws:kms:[a-z0-9-]+:\d{12}:key/[A-Za-z0-9/_+=,.@-]+\b", ForbiddenValueRegexOptions)),
        ("kms_alias", new Regex(@"\balias/hush[-/A-Za-z0-9_+=,.@]+\b", ForbiddenValueRegexOptions)),
        ("destroyed_admin_only_scalar_marker", new Regex(@"\[destroyed-admin-only-protected-tally-scalar\]", ForbiddenValueRegexOptions)),
    ];

    private static readonly (string Marker, Regex Pattern)[] RestrictedPackageForbiddenValuePatterns =
    [
        ("aws_access_key_id", new Regex(@"\b(?:AKIA|ASIA)[0-9A-Z]{16}\b", ForbiddenValueRegexOptions)),
        ("aws_secret_access_key", new Regex(@"\bAWS_SECRET_ACCESS_KEY\b|\baws_secret_access_key\b", ForbiddenValueRegexOptions)),
        ("aws_session_token", new Regex(@"\bAWS_SESSION_TOKEN\b|\baws_session_token\b", ForbiddenValueRegexOptions)),
        ("private_key_pem", new Regex(@"-----BEGIN [A-Z ]*PRIVATE KEY-----", ForbiddenValueRegexOptions)),
        ("plaintext_tally_scalar", new Regex(@"\bplaintext(?:Tally)?Scalar\b|\btallyPrivateScalar\b|\bprivateScalar\b", ForbiddenValueRegexOptions)),
    ];

    public static IReadOnlySet<string> PublicPackageForbiddenFieldNames { get; } = new HashSet<string>(
        [
            "named_roster",
            "roster",
            "roster_entries",
            "rosterEntry",
            "restricted_roster",
            "restrictedRoster",
            "organization_voter_id",
            "organizationVoterId",
            "stable_voter_id",
            "stableVoterId",
            "voter_id",
            "voterId",
            "voter_name",
            "voterName",
            "contact_value",
            "contactValue",
            "contact_match_key",
            "contactMatchKey",
            "recipient_contact_hash",
            "recipientContactHash",
            "linked_actor_public_address",
            "linkedActorPublicAddress",
            "eligibility_link_id",
            "eligibilityLinkId",
            "identity_code",
            "identityCode",
            "code_challenge_hash",
            "codeChallengeHash",
            "provider_message_id",
            "providerMessageId",
            "checkoff_id",
            "checkoffId",
            "checkoff_record_id",
            "checkoffRecordId",
            "link_id",
            "linkId",
            "actor_public_address",
            "actorPublicAddress",
            "trustee_account_id",
            "trusteeAccountId",
            "trustee_person_ref",
            "trusteePersonRef",
            "custody_domain_ref_hash",
            "custodyDomainRefHash",
            "admin_domain_ref_hash",
            "adminDomainRefHash",
            "legal_entity_ref_hash",
            "legalEntityRefHash",
            "ip_address",
            "ipAddress",
            "device_id",
            "deviceId",
            "device_identifier",
            "deviceIdentifier",
            "installation_id",
            "installationId",
            "attestation_token",
            "attestationToken",
            "raw_attestation_token",
            "rawAttestationToken",
            "host_private_state",
            "hostPrivateState",
            "deployment_secret",
            "deploymentSecret",
            "support_correlation_id",
            "supportCorrelationId",
            "debug_correlation_id",
            "debugCorrelationId",
            "plaintext_vote",
            "plaintextVote",
            "vote_secret",
            "voteSecret",
            "raw_trustee_share",
            "rawTrusteeShare",
            "private_key",
            "privateKey",
            "final_cast_randomness",
            "finalCastRandomness",
            "accepted_to_published_mapping",
            "acceptedToPublishedMapping",
            "hidden_permutation",
            "hiddenPermutation",
            "permutation",
            "shuffle_map",
            "shuffleMap",
            "rerandomization_randomness",
            "rerandomizationRandomness",
            "private_randomness",
            "privateRandomness",
            "raw_witness",
            "rawWitness",
            "witness_material",
            "witnessMaterial",
            "sealed_witness_material",
            "sealedWitnessMaterial",
            "proof_witness",
            "proofWitness",
            "full_report",
            "fullReport",
            "full_report_body",
            "fullReportBody",
            "reviewer_workpaper",
            "reviewerWorkpaper",
            "workpaper",
            "workpapers",
            "finding_body",
            "findingBody",
            "finding_detail",
            "findingDetail",
            "retest_evidence_body",
            "retestEvidenceBody",
            "confidential_report_url",
            "confidentialReportUrl",
            "raw_log_line",
            "rawLogLine",
            "raw_log_body",
            "rawLogBody",
            "raw_audit_log",
            "rawAuditLog",
            "raw_provider_event",
            "rawProviderEvent",
            "provider_account_id",
            "providerAccountId",
            "kms_key_id",
            "kmsKeyId",
            "kms_key_arn",
            "kmsKeyArn",
            "kms_alias",
            "kmsAlias",
            "kms_raw_tag_set",
            "kmsRawTagSet",
            "sealed_tally_private_scalar",
            "sealedTallyPrivateScalar",
            "sealed_envelope",
            "sealedEnvelope",
            "raw_sealed_scalar",
            "rawSealedScalar",
            "plaintext_tally_scalar",
            "plaintextTallyScalar",
            "tally_private_scalar",
            "tallyPrivateScalar",
            "decrypt_authority",
            "decryptAuthority",
            "kms_plaintext_key",
            "kmsPlaintextKey",
            "kms_unwrapped_key",
            "kmsUnwrappedKey",
            "executor_private_key",
            "executorPrivateKey",
            "iam_policy_document",
            "iamPolicyDocument",
            "security_group_rule_dump",
            "securityGroupRuleDump",
            "raw_backup_archive",
            "rawBackupArchive",
            "incident_workpaper",
            "incidentWorkpaper",
            "regulatory_workpaper",
            "regulatoryWorkpaper",
            "jurisdiction_workpaper",
            "jurisdictionWorkpaper",
            "authority_private_correspondence",
            "authorityPrivateCorrespondence",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static IReadOnlySet<string> Sp05PublicEligibilityForbiddenFieldNames { get; } = new HashSet<string>(
        PublicPackageForbiddenFieldNames.Concat(
        [
            "display_label",
            "displayLabel",
        ]),
        StringComparer.OrdinalIgnoreCase);

    public static bool IsForbiddenInPublicPackage(string fieldName) =>
        PublicPackageForbiddenFieldNames.Contains(NormalizeFieldName(fieldName));

    public static bool IsForbiddenInSp05PublicEligibilityArtifact(string fieldName) =>
        Sp05PublicEligibilityForbiddenFieldNames.Contains(NormalizeFieldName(fieldName));

    public static IReadOnlyList<string> FindForbiddenPublicFields(IEnumerable<string> fieldNames)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);

        return fieldNames
            .Select(NormalizeFieldName)
            .Where(PublicPackageForbiddenFieldNames.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static IReadOnlyList<string> FindForbiddenPublicMaterialValues(string? text) =>
        FindForbiddenMaterialValues(text, PublicPackageForbiddenValuePatterns);

    public static IReadOnlyList<string> FindForbiddenRestrictedMaterialValues(string? text) =>
        FindForbiddenMaterialValues(text, RestrictedPackageForbiddenValuePatterns);

    public static IReadOnlyList<string> FindForbiddenSp05PublicFields(IEnumerable<string> fieldNames)
    {
        ArgumentNullException.ThrowIfNull(fieldNames);

        return fieldNames
            .Select(NormalizeFieldName)
            .Where(Sp05PublicEligibilityForbiddenFieldNames.Contains)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static bool IsRestrictedArtifactPath(string relativePath) =>
        NormalizePath(relativePath).StartsWith(
            $"{VerificationPackageFileNames.RestrictedDirectory}/",
            StringComparison.OrdinalIgnoreCase);

    public static bool IsRestrictedArtifactEntry(AuditPackageManifestEntryRecord entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return entry.Visibility == VerificationArtifactVisibility.Restricted ||
            IsRestrictedArtifactPath(entry.Path);
    }

    private static string NormalizeFieldName(string fieldName) =>
        (fieldName ?? string.Empty).Trim();

    private static string NormalizePath(string relativePath) =>
        (relativePath ?? string.Empty).Replace('\\', '/').TrimStart('/');

    private static IReadOnlyList<string> FindForbiddenMaterialValues(
        string? text,
        IReadOnlyList<(string Marker, Regex Pattern)> patterns)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return patterns
            .Where(pattern => pattern.Pattern.IsMatch(text))
            .Select(pattern => pattern.Marker)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}

