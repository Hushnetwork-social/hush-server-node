namespace HushShared.Feeds.Model;

/// <summary>
/// Specifies what triggered a Group Feed key rotation.
/// Used for audit trail and understanding key generation history.
/// </summary>
public enum RotationTrigger
{
    /// <summary>A new member joined the group (public join or invitation accepted).</summary>
    Join = 0,

    /// <summary>A member voluntarily left the group.</summary>
    Leave = 1,

    /// <summary>A member was banned from the group (cryptographic removal).</summary>
    Ban = 2,

    /// <summary>A previously banned member was reinstated.</summary>
    Unban = 3,

    /// <summary>Manual key rotation triggered by an administrator.</summary>
    Manual = 4
}
