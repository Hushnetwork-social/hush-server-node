using System.Text;
using System.Text.Json.Nodes;

namespace OperationalEvidencePromoter;

public static class OperationalEvidenceMarkdownRenderer
{
    public static string RenderPublicSummary(
        JsonObject run,
        OperationalEvidenceCheckSetResult checkResult,
        IReadOnlyList<OperationalEvidenceGeneratedArtifact> artifacts)
    {
        var publicArtifacts = artifacts
            .Where(artifact => artifact.Visibility == OperationalEvidenceArtifactVisibility.Public)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# FEAT-133 Public Operational Summary");
        builder.AppendLine();
        builder.AppendLine($"Run: {checkResult.RunId}");
        builder.AppendLine($"Claim level: {GetString(run, "claimLevel")}");
        builder.AppendLine($"Status: {checkResult.Status}");
        builder.AppendLine();
        builder.AppendLine("## Check Summary");
        foreach (var check in checkResult.Checks.OrderBy(check => check.CheckId, StringComparer.Ordinal))
        {
            builder.AppendLine($"- {check.CheckId}: {check.Status} - {check.Reason}");
        }

        builder.AppendLine();
        builder.AppendLine("## Warnings And Blockers");
        builder.AppendLine($"Warnings: {FormatList(checkResult.Warnings)}");
        builder.AppendLine($"Blockers: {FormatList(checkResult.Blockers)}");
        builder.AppendLine($"Not applicable: {FormatList(checkResult.NotApplicable)}");
        builder.AppendLine();
        builder.AppendLine("## Public Artifact Refs");
        foreach (var artifact in publicArtifacts)
        {
            builder.AppendLine($"- {artifact.RelativePath}: sha256:{artifact.Sha256Hash}");
        }

        builder.AppendLine();
        builder.AppendLine("## Residual Risk");
        if (run["residualRisk"] is JsonArray residualRisk && residualRisk.Count > 0)
        {
            foreach (var item in residualRisk)
            {
                builder.AppendLine($"- {item?.GetValue<string>()}");
            }
        }
        else
        {
            builder.AppendLine("- None recorded.");
        }

        builder.AppendLine();
        builder.AppendLine("## Claim Effect");
        builder.AppendLine(GetString(run, "claimEffect") ?? "No claim effect recorded.");
        return OperationalEvidenceCanonicalJson.NormalizeLineEndings(builder.ToString());
    }

    public static string RenderRestrictedIndex(
        JsonObject run,
        IReadOnlyList<OperationalEvidenceGeneratedArtifact> artifacts)
    {
        var restrictedArtifacts = artifacts
            .Where(artifact => artifact.Visibility == OperationalEvidenceArtifactVisibility.Restricted)
            .OrderBy(artifact => artifact.RelativePath, StringComparer.Ordinal)
            .ToArray();
        var builder = new StringBuilder();
        builder.AppendLine("# FEAT-133 Restricted Operational Evidence Index");
        builder.AppendLine();
        builder.AppendLine($"Run: {GetString(run, "runId")}");
        builder.AppendLine("Review scope: access control, logging, backup and restore, incident declaration, and auditor-room access evidence.");
        builder.AppendLine();
        builder.AppendLine("## Restricted Artifacts");
        foreach (var artifact in restrictedArtifacts)
        {
            builder.AppendLine($"- {artifact.RelativePath}: sha256:{artifact.Sha256Hash}");
        }

        builder.AppendLine();
        builder.AppendLine("## Review Instructions");
        builder.AppendLine("- Confirm each restricted artifact hash matches the package or HushDocuments copy.");
        builder.AppendLine("- Confirm public package files reference restricted evidence by id or hash only.");
        builder.AppendLine("- Do not copy credentials, raw vote data, trustee secret material, private tally material, or raw logs into public evidence.");
        return OperationalEvidenceCanonicalJson.NormalizeLineEndings(builder.ToString());
    }

    private static string FormatList(IReadOnlyCollection<string> values) =>
        values.Count == 0 ? "none" : string.Join(", ", values.OrderBy(value => value, StringComparer.Ordinal));

    private static string? GetString(JsonObject? obj, string propertyName)
    {
        if (obj is null)
        {
            return null;
        }

        try
        {
            return obj[propertyName]?.GetValue<string>();
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
