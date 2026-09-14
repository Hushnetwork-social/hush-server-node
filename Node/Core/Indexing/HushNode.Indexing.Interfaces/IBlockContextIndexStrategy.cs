using HushShared.Blockchain.TransactionModel;

namespace HushNode.Indexing.Interfaces;

/// <summary>FEAT-015 D6 block-context payload for index strategies.</summary>
public sealed record BlockIndexContext(long BlockIndex, DateTime BlockCreationTimeUtc);

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
