using System.Text.Json;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Blockchain.Storage;
using HushNode.Events;
using HushNode.Idempotency;
using HushNode.Interfaces.Models;
using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Olimpo;

namespace HushNode.Blockchain.gRPC;

public class BlockchainGrpcService(
    IBlockchainStorageService blockchainStorageService,
    IEnumerable<ITransactionContentHandler> transactionContentHandlers,
    IMemPoolService memPoolService,
    IEventAggregator eventAggregator,
    IIdempotencyService idempotencyService) : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
    private readonly IEnumerable<ITransactionContentHandler> _transactionContentHandlers = transactionContentHandlers;
    private readonly IMemPoolService _memPoolService = memPoolService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly IIdempotencyService _idempotencyService = idempotencyService;

    public override async Task<GetBlockchainHeightReply> GetBlockchainHeight(
        GetBlockchainHeightRequest request,
        ServerCallContext context)
    {
        var blockchainState = await this._blockchainStorageService.RetrieveCurrentBlockchainStateAsync();

        return new GetBlockchainHeightReply
        {
            Index = blockchainState.BlockIndex.Value
        };
    }

    public override async Task<SubmitSignedTransactionReply> SubmitSignedTransaction(
        SubmitSignedTransactionRequest request,
        ServerCallContext context)
    {
        var message = string.Empty;
        var successful = false;
        var status = TransactionStatus.Unspecified;

        var transaction = JsonSerializer.Deserialize<AbstractTransaction>(request.SignedTransaction)
            ?? throw new InvalidDataException("Transaction invalid or without handler");

        if (this.ValidateUserSignature(transaction))
        {
            // FEAT-057: Check idempotency for FeedMessage transactions BEFORE content validation
            var messageId = this.TryExtractMessageId(transaction);
            if (messageId is { } feedMessageId)
            {
                var idempotencyResult = await this._idempotencyService.CheckAsync(feedMessageId);

                // Handle non-ACCEPTED results (Pending, AlreadyExists, Rejected)
                if (idempotencyResult != IdempotencyCheckResult.Accepted)
                {
                    // Pending and AlreadyExists are success cases (message will be/is confirmed)
                    successful = idempotencyResult != IdempotencyCheckResult.Rejected;
                    status = MapToTransactionStatus(idempotencyResult);
                    message = idempotencyResult switch
                    {
                        IdempotencyCheckResult.Pending => "Message is already pending in MemPool",
                        IdempotencyCheckResult.AlreadyExists => "Message already exists in the blockchain",
                        IdempotencyCheckResult.Rejected => "Transaction rejected due to server error",
                        _ => "Unknown status"
                    };

                    return new SubmitSignedTransactionReply
                    {
                        Successfull = successful,
                        Message = message,
                        Status = status
                    };
                }
            }

            foreach (var item in this._transactionContentHandlers)
            {
                if (item.CanValidate(transaction.PayloadKind))
                {
                    var transactionSignedByValidator = item.ValidateAndSign(transaction);

                    if (transactionSignedByValidator == null)
                    {
                        successful = false;
                        status = TransactionStatus.Rejected;
                        message = "Transaction is invalid and was not added to the MemPool";
                    }
                    else
                    {
                        // FEAT-057: Track message ID in MemPool for FeedMessage transactions
                        if (messageId is { } msgId)
                        {
                            // Atomic tracking - if TryTrackInMemPool returns false, a concurrent
                            // request already added it, so we return PENDING instead of duplicating
                            if (!this._idempotencyService.TryTrackInMemPool(msgId))
                            {
                                return new SubmitSignedTransactionReply
                                {
                                    Successfull = true,
                                    Message = "Message is already pending in MemPool (concurrent submission)",
                                    Status = TransactionStatus.Pending
                                };
                            }
                        }

                        // add the transaction to the MemPool
                        this._memPoolService.AddVerifiedTransaction(transactionSignedByValidator);
                        Console.WriteLine($"[E2E] Transaction added to mempool: {transactionSignedByValidator.TransactionId}");

                        // notify that a transaction was received (to resume block production if paused)
                        Console.WriteLine($"[E2E] Publishing TransactionReceivedEvent: {transactionSignedByValidator.TransactionId}");
                        _ = this._eventAggregator.PublishAsync(new TransactionReceivedEvent(transactionSignedByValidator.TransactionId));

                        successful = true;
                        status = TransactionStatus.Accepted;
                        message = "Transaction validated and added to MemPool";
                    }

                    break;
                }
            }
        }

        return new SubmitSignedTransactionReply
        {
            Successfull = successful,
            Message = message,
            Status = status
        };
    }

    /// <summary>
    /// FEAT-057: Extracts message ID from FeedMessage transaction payloads.
    /// Returns null for non-FeedMessage transactions.
    /// </summary>
    private FeedMessageId? TryExtractMessageId(AbstractTransaction transaction)
    {
        // Check if this is a NewFeedMessagePayload (personal/chat feed message)
        if (transaction is SignedTransaction<NewFeedMessagePayload> feedMessageTx)
        {
            return feedMessageTx.Payload.FeedMessageId;
        }

        // Group feed messages now use the same NewFeedMessagePayload with KeyGeneration set

        // Not a FeedMessage transaction - no idempotency check needed
        return null;
    }

    /// <summary>
    /// FEAT-057: Maps IdempotencyCheckResult to TransactionStatus for gRPC response.
    /// </summary>
    private static TransactionStatus MapToTransactionStatus(IdempotencyCheckResult result)
    {
        return result switch
        {
            IdempotencyCheckResult.Accepted => TransactionStatus.Accepted,
            IdempotencyCheckResult.AlreadyExists => TransactionStatus.AlreadyExists,
            IdempotencyCheckResult.Pending => TransactionStatus.Pending,
            IdempotencyCheckResult.Rejected => TransactionStatus.Rejected,
            _ => TransactionStatus.Unspecified
        };
    }

    private bool ValidateUserSignature(AbstractTransaction transaction)
    {
        return true;
    }
}
