namespace HushShared.Elections.Verification.Model;

public sealed partial class HushVotingPackageVerifier
{
    private static async Task<IReadOnlyList<VerifierCheckResultRecord>> CheckPrivacyBoundaryAsync(
        string packagePath,
        AuditPackageManifestRecord manifest,
        CancellationToken cancellationToken)
    {
        if (manifest.PackageView != VerificationPackageView.PublicAnonymous)
        {
            return
            [
                CreateResult(
                    "VFY-PRIVACY-000",
                    VerificationCheckStatus.NotApplicable,
                    VerificationResultCodes.PackageStructureValid,
                    "Public privacy-boundary check is not applicable to restricted packages."),
            ];
        }

        var forbiddenFields = new List<string>();
        foreach (var entry in manifest.Entries)
        {
            if (VerificationPrivacyBoundary.IsRestrictedArtifactEntry(entry))
            {
                forbiddenFields.Add(entry.Path);
                continue;
            }

            if (!entry.MediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var text = await File.ReadAllTextAsync(ResolvePackagePath(packagePath, entry.Path), cancellationToken);
            forbiddenFields.AddRange(VerificationPrivacyBoundary.FindForbiddenPublicFields(CollectJsonPropertyNames(text)));
        }

        var distinctFields = forbiddenFields
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (distinctFields.Length > 0)
        {
            return
            [
                CreateResult(
                    "VFY-PRIVACY-001",
                    VerificationCheckStatus.Fail,
                    VerificationResultCodes.PublicRestrictedFieldLeak,
                    "Public package contains restricted evidence fields.",
                    distinctFields.ToDictionary(x => x, x => "forbidden", StringComparer.OrdinalIgnoreCase)),
            ];
        }

        return
        [
            CreateResult(
                "VFY-PRIVACY-000",
                VerificationCheckStatus.Pass,
                VerificationResultCodes.PackageStructureValid,
                "Public package contains no known restricted evidence fields."),
        ];
    }
}

