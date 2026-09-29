namespace HushShared.Elections.Model;

/// <summary>Append-only proof of successful indexing, correlated to retained chain bytes.
/// This is not the chain head, a permission token or a checkpoint that operators may seed.</summary>
public sealed record ElectionIndexCheckpoint(long BlockHeight, Guid BlockId, string BlockHash, string HistoryDigestSha256,
    string PolicyVersion = ElectionEntitlementCapture.CurrentPolicyVersion);
