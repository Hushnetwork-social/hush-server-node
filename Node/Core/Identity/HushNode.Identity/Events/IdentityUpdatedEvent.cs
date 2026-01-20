namespace HushNode.Identity.Events;

/// <summary>
/// Event published when a user's identity profile is updated.
/// Used to trigger cache invalidation in IdentityCacheService.
/// </summary>
/// <param name="PublicSigningAddress">The public signing address of the identity that was updated.</param>
public record IdentityUpdatedEvent(string PublicSigningAddress);
