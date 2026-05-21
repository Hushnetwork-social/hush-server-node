namespace HushNode.Caching;

public static class ElectionVoidPublicCacheConstants
{
    public static readonly TimeSpan CacheTtl = TimeSpan.FromDays(7);

    public static string GetPublicStatusKey(string electionId) =>
        $"hushvoting:void:public-status:{NormalizeKeyPart(electionId, nameof(electionId))}";

    public static string GetPublicArtifactKey(
        string electionId,
        Guid voidDecisionId,
        Guid publicationAttemptId,
        string artifactName) =>
        string.Join(
            ':',
            "hushvoting:void:public-package",
            NormalizeKeyPart(electionId, nameof(electionId)),
            voidDecisionId.ToString("N"),
            publicationAttemptId.ToString("N"),
            NormalizeArtifactName(artifactName));

    private static string NormalizeKeyPart(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();

    private static string NormalizeArtifactName(string artifactName) =>
        NormalizeKeyPart(artifactName, nameof(artifactName))
            .Replace('\\', '/')
            .Replace(':', '_');
}
