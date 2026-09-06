using HushShared.Blockchain.TransactionModel;

namespace HushNode.Indexing.Interfaces;

/// <summary>
/// FEAT-015 D6 — optional block-context index seam (additive). Strategies that implement this
/// interface receive the containing block's consensus timestamp so an indexed assignment's
/// effective-from can equal the authoritative block time. Existing <see cref="IIndexStrategy"/>
/// strategies are untouched and keep their current behavior.
/// </summary>
public interface IBlockContextIndexStrategy
{
    bool CanHandle(AbstractTransaction transaction);

    Task HandleAsync(AbstractTransaction transaction, DateTime blockCreationTimeUtc);
}
