namespace PublicStateElectionPrerequisiteRegisterPromoter;

public sealed record PublicStatePublicOutputFinding(
    string RelativePath,
    string Category,
    string Evidence);

public static class PublicStateElectionPrerequisitePublicOutputScanner
{
    public static IReadOnlyList<PublicStatePublicOutputFinding> Scan(
        IEnumerable<(string RelativePath, string Content)> publicOutputs)
    {
        var findings = new List<PublicStatePublicOutputFinding>();
        foreach (var output in publicOutputs)
        {
            foreach (var forbidden in PublicStateElectionPrerequisiteGateChecker.ForbiddenPublicClaimNeedles)
            {
                if (output.Content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new PublicStatePublicOutputFinding(
                        output.RelativePath,
                        "forbidden_public_claim",
                        forbidden));
                }
            }

            foreach (var forbidden in PublicStateElectionPrerequisiteGateChecker.ForbiddenRestrictedMaterialNeedles)
            {
                if (output.Content.Contains(forbidden, StringComparison.OrdinalIgnoreCase))
                {
                    findings.Add(new PublicStatePublicOutputFinding(
                        output.RelativePath,
                        "restricted_material",
                        forbidden));
                }
            }
        }

        return findings
            .OrderBy(finding => finding.RelativePath, StringComparer.Ordinal)
            .ThenBy(finding => finding.Category, StringComparer.Ordinal)
            .ThenBy(finding => finding.Evidence, StringComparer.Ordinal)
            .ToArray();
    }

    public static void EnsurePublicOutputsSafe(
        IEnumerable<(string RelativePath, string Content)> publicOutputs)
    {
        var findings = Scan(publicOutputs);
        if (findings.Count == 0)
        {
            return;
        }

        throw new PublicStateElectionPrerequisitePromotionException(
            "Public/state prerequisite public output validation failed.",
            findings.Select(finding =>
                $"{finding.RelativePath}: {finding.Category}: {finding.Evidence}").ToArray());
    }
}
