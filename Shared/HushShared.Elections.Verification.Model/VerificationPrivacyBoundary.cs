namespace HushShared.Elections.Verification.Model;

public static class VerificationPrivacyBoundary
{
    public static IReadOnlySet<string> PublicPackageForbiddenFieldNames { get; } = new HashSet<string>(
        [
            "named_roster",
            "roster",
            "roster_entries",
            "rosterEntry",
            "organization_voter_id",
            "organizationVoterId",
            "stable_voter_id",
            "stableVoterId",
            "voter_id",
            "voterId",
            "voter_name",
            "voterName",
            "linked_actor_public_address",
            "linkedActorPublicAddress",
            "actor_public_address",
            "actorPublicAddress",
            "ip_address",
            "ipAddress",
            "support_correlation_id",
            "supportCorrelationId",
            "debug_correlation_id",
            "debugCorrelationId",
            "plaintext_vote",
            "plaintextVote",
            "raw_trustee_share",
            "rawTrusteeShare",
            "private_key",
            "privateKey",
            "final_cast_randomness",
            "finalCastRandomness",
            "accepted_to_published_mapping",
            "acceptedToPublishedMapping",
        ],
        StringComparer.OrdinalIgnoreCase);

    public static bool IsForbiddenInPublicPackage(string fieldName) =>
        PublicPackageForbiddenFieldNames.Contains(NormalizeFieldName(fieldName));

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
}

