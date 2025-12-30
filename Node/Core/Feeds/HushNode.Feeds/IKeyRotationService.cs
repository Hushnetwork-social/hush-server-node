using HushShared.Feeds.Model;

namespace HushNode.Feeds;

/// <summary>
/// Service for triggering key rotations in Group Feeds.
/// Used by membership change handlers to rotate encryption keys when members join, leave, or are banned.
/// </summary>
public interface IKeyRotationService
{
    /// <summary>
    /// Triggers a key rotation for a Group Feed.
    /// Generates a new AES key, encrypts it for all current active members, and creates a KeyRotation transaction.
    /// </summary>
    /// <param name="feedId">The Group Feed to rotate keys for.</param>
    /// <param name="trigger">The event that triggered the rotation (Join, Leave, Ban, Unban, Manual).</param>
    /// <param name="joiningMemberAddress">Optional: Public address of a member joining (will be included in key distribution).</param>
    /// <param name="leavingMemberAddress">Optional: Public address of a member leaving/banned (will be excluded from key distribution).</param>
    /// <returns>Result containing the new KeyGeneration number or error details.</returns>
    Task<KeyRotationResult> TriggerRotationAsync(
        FeedId feedId,
        RotationTrigger trigger,
        string? joiningMemberAddress = null,
        string? leavingMemberAddress = null);
}

/// <summary>
/// Result of a key rotation operation.
/// </summary>
public record KeyRotationResult
{
    /// <summary>Whether the key rotation was successful.</summary>
    public bool IsSuccess { get; init; }

    /// <summary>The new KeyGeneration number (only set on success).</summary>
    public int? NewKeyGeneration { get; init; }

    /// <summary>The GroupFeedKeyRotationPayload ready for blockchain submission (only set on success).</summary>
    public GroupFeedKeyRotationPayload? Payload { get; init; }

    /// <summary>Error message (only set on failure).</summary>
    public string? ErrorMessage { get; init; }

    private KeyRotationResult() { }

    /// <summary>Creates a successful result.</summary>
    public static KeyRotationResult Success(int newKeyGeneration, GroupFeedKeyRotationPayload payload) =>
        new()
        {
            IsSuccess = true,
            NewKeyGeneration = newKeyGeneration,
            Payload = payload
        };

    /// <summary>Creates a failure result.</summary>
    public static KeyRotationResult Failure(string errorMessage) =>
        new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage
        };
}
