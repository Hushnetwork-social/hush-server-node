namespace HushNode.Elections;

/// <summary>Host-owned block facts. Never deserialized from an election payload or public request.</summary>
public sealed record ElectionExecutionContext(
    Guid TransactionId, Guid BlockId, long BlockHeight, int TransactionPosition, DateTime BlockTimeUtc)
{
    public bool IsValid => TransactionId != Guid.Empty && BlockId != Guid.Empty && BlockHeight >= 0 &&
        TransactionPosition >= 0 && BlockTimeUtc.Kind == DateTimeKind.Utc;
}

/// <summary>Index dispatch supplies the trusted context; direct service calls without it fail closed.</summary>
public interface IElectionExecutionContextSource
{
    ElectionExecutionContext? Current { get; }
}

/// <summary>Adapts the canonical dispatcher frame for both direct and encrypted election paths.</summary>
public sealed class ElectionExecutionContextSource : IElectionExecutionContextSource
{
    public ElectionExecutionContext? Current => HushNode.Indexing.Interfaces.BlockTransactionExecutionScope.Current is { } frame
        ? new(frame.TransactionId, frame.Block.BlockId, frame.Block.BlockIndex,
            frame.Block.TransactionPosition, frame.Block.BlockCreationTimeUtc)
        : null;
}
