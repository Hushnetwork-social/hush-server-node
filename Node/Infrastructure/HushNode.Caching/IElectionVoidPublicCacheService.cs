namespace HushNode.Caching;

/// <summary>
/// Redis acceleration for public FEAT-138 VOID status and artifact payloads.
/// PostgreSQL/report-package storage remains authoritative.
/// </summary>
public interface IElectionVoidPublicCacheService
{
    Task<ElectionVoidPublicCacheEnvelope?> GetPublicStatusAsync(string electionId);

    Task<ElectionVoidPublicCacheEnvelope?> GetPublicArtifactAsync(
        string electionId,
        Guid voidDecisionId,
        Guid publicationAttemptId,
        string artifactName);

    Task SetPublicStatusAsync(
        string electionId,
        ElectionVoidPublicCacheEnvelope envelope);

    Task SetPublicArtifactAsync(
        string electionId,
        Guid voidDecisionId,
        Guid publicationAttemptId,
        string artifactName,
        ElectionVoidPublicCacheEnvelope envelope);
}

public sealed record ElectionVoidPublicCacheEnvelope(
    string ContentType,
    string Sha256Hash,
    Guid PublicationAttemptId,
    DateTime CachedAt,
    string? PayloadText = null,
    string? PayloadBase64 = null)
{
    public string ContentType { get; init; } = NormalizeRequired(ContentType, nameof(ContentType));

    public string Sha256Hash { get; init; } = NormalizeRequired(Sha256Hash, nameof(Sha256Hash));

    public string? PayloadText { get; init; } = NormalizeOptional(PayloadText);

    public string? PayloadBase64 { get; init; } = NormalizeOptional(PayloadBase64);

    public byte[]? GetPayloadBytes() =>
        string.IsNullOrWhiteSpace(PayloadBase64)
            ? null
            : Convert.FromBase64String(PayloadBase64);

    private static string NormalizeRequired(string value, string paramName) =>
        string.IsNullOrWhiteSpace(value)
            ? throw new ArgumentException("Value is required.", paramName)
            : value.Trim();

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
