namespace HushNode.Events;

/// <summary>
/// Event published when a user's identity profile is updated.
/// Used by IdentityCacheService to trigger cache invalidation.
/// </summary>
/// <param name="PublicSigningAddress">The public signing address of the identity that was updated.</param>
public record IdentityUpdatedEvent(string PublicSigningAddress);
