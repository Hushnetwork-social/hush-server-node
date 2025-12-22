using System.Numerics;
using HushShared.Feeds.Model;
using HushNode.Reactions.Crypto;

namespace HushNode.Reactions;

/// <summary>
/// Stub implementation of IFeedInfoProvider for initial development.
/// This should be replaced with a real implementation that integrates with the Feeds module.
/// </summary>
public class StubFeedInfoProvider : IFeedInfoProvider
{
    private readonly IBabyJubJub _curve;

    public StubFeedInfoProvider(IBabyJubJub curve)
    {
        _curve = curve;
    }

    public Task<ECPoint?> GetFeedPublicKeyAsync(FeedId feedId)
    {
        // TODO: Integrate with Feeds module to get the actual feed public key
        // For now, return the generator point as a placeholder
        return Task.FromResult<ECPoint?>(_curve.Generator);
    }

    public Task<byte[]?> GetAuthorCommitmentAsync(FeedMessageId messageId)
    {
        // TODO: Integrate with Feeds module to get the actual author commitment
        // For now, return a placeholder value
        var placeholder = new byte[32];
        placeholder[31] = 1; // Non-zero value
        return Task.FromResult<byte[]?>(placeholder);
    }
}
