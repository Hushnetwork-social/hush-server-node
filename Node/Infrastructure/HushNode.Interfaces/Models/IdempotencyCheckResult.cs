namespace HushNode.Interfaces.Models;

/// <summary>
/// Result of an idempotency check for feed messages.
/// FEAT-057: Server Message Idempotency.
/// </summary>
public enum IdempotencyCheckResult
{
    /// <summary>
    /// Message is new and can be processed.
    /// </summary>
    Accepted,

    /// <summary>
    /// Message already exists in the database (confirmed).
    /// </summary>
    AlreadyExists,

    /// <summary>
    /// Message is currently in the MemPool (awaiting confirmation).
    /// </summary>
    Pending,

    /// <summary>
    /// Transaction rejected due to a server error (fail-closed).
    /// </summary>
    Rejected
}
