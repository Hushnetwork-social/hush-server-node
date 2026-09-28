using HushShared.Blockchain.TransactionModel;

namespace HushNode.Indexing.Interfaces;

/// <summary>FEAT-015 D6 block-context payload for index strategies.</summary>
public sealed record BlockIndexContext(long BlockIndex, DateTime BlockCreationTimeUtc,
    Guid BlockId = default, int TransactionPosition = -1);

/// <summary>Canonical dispatch frame, scoped to one transaction and its asynchronous children.</summary>
public sealed record BlockTransactionExecution(Guid TransactionId, BlockIndexContext Block);

/// <summary>
/// Only host dispatch installs a frame. No serialized request or payload supplies this authority.
/// AsyncLocal keeps concurrent node/scenario executions isolated; disposal restores the parent.
/// </summary>
public static class BlockTransactionExecutionScope
{
    private static readonly AsyncLocal<BlockTransactionExecution?> Slot = new();
    public static BlockTransactionExecution? Current => Slot.Value;

    public static IDisposable Enter(BlockTransactionExecution execution)
    {
        var previous = Slot.Value;
        Slot.Value = execution;
        return new Scope(previous);
    }

    private sealed class Scope(BlockTransactionExecution? previous) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            Slot.Value = previous;
            _disposed = true;
        }
    }
}

/// <summary>
/// FEAT-015 D6 — optional block-context index seam (additive). Strategies that implement this
/// interface receive the containing block's index and consensus timestamp so an indexed
/// assignment's effective-from and provenance equal authoritative block facts. Existing
/// <see cref="IIndexStrategy"/> strategies are untouched and keep their current behavior.
/// </summary>
public interface IBlockContextIndexStrategy
{
    bool CanHandle(AbstractTransaction transaction);

    Task HandleAsync(AbstractTransaction transaction, BlockIndexContext blockContext);
}
