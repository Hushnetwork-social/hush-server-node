using System.Collections.Concurrent;
using HushNode.Idempotency;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;

namespace HushNode.MemPool;

public class MemPoolService(IIdempotencyService idempotencyService) : IMemPoolService
{
    private readonly IIdempotencyService _idempotencyService = idempotencyService;
    private ConcurrentBag<AbstractTransaction> _nextBlockTransactionsCandidate = [];

    public IEnumerable<AbstractTransaction> GetPendingValidatedTransactionsAsync()
    {
        var transactions = this._nextBlockTransactionsCandidate.TakeAndRemove(1000).ToList();

        // FEAT-057: Extract message IDs from FeedMessage transactions and clean up tracking
        if (transactions.Count > 0)
        {
            var messageIds = ExtractMessageIds(transactions);
            if (messageIds.Count > 0)
            {
                this._idempotencyService.RemoveFromTracking(messageIds);
            }
        }

        return transactions;
    }

    public void AddVerifiedTransaction(AbstractTransaction validatedTransaction) =>
        this._nextBlockTransactionsCandidate.Add(validatedTransaction);

    public Task InitializeMemPoolAsync()
    {
        // TODO [AboimPinto]: In case of beeing part of an established network,
        //                    the mempool should be initialized with the pending transactions from the other nodes.
        return Task.CompletedTask;
    }

    /// <summary>
    /// FEAT-057: Extracts message IDs from FeedMessage transactions.
    /// Returns empty list for non-FeedMessage transactions.
    /// </summary>
    private static List<FeedMessageId> ExtractMessageIds(IEnumerable<AbstractTransaction> transactions)
    {
        var messageIds = new List<FeedMessageId>();

        foreach (var transaction in transactions)
        {
            // Check if this is a NewFeedMessagePayload (personal/chat feed message)
            if (transaction is SignedTransaction<NewFeedMessagePayload> feedMessageTx)
            {
                messageIds.Add(feedMessageTx.Payload.FeedMessageId);
            }
            // Check if this is a NewGroupFeedMessagePayload (group feed message)
            else if (transaction is SignedTransaction<NewGroupFeedMessagePayload> groupMessageTx)
            {
                messageIds.Add(groupMessageTx.Payload.MessageId);
            }
        }

        return messageIds;
    }
}
