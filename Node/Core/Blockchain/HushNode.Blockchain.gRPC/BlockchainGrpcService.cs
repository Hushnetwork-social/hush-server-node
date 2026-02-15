using System.Text.Json;
using Grpc.Core;
using HushNetwork.proto;
using HushNode.Blockchain.Storage;
using HushNode.Events;
using HushNode.Feeds.Storage;
using HushNode.Idempotency;
using HushNode.Interfaces.Models;
using HushNode.MemPool;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Olimpo;

namespace HushNode.Blockchain.gRPC;

public class BlockchainGrpcService(
    IBlockchainStorageService blockchainStorageService,
    IEnumerable<ITransactionContentHandler> transactionContentHandlers,
    IMemPoolService memPoolService,
    IEventAggregator eventAggregator,
    IIdempotencyService idempotencyService,
    IAttachmentTempStorageService attachmentTempStorageService,
    ILogger<BlockchainGrpcService> logger) : HushBlockchain.HushBlockchainBase
{
    private readonly IBlockchainStorageService _blockchainStorageService = blockchainStorageService;
    private readonly IEnumerable<ITransactionContentHandler> _transactionContentHandlers = transactionContentHandlers;
    private readonly IMemPoolService _memPoolService = memPoolService;
    private readonly IEventAggregator _eventAggregator = eventAggregator;
    private readonly IIdempotencyService _idempotencyService = idempotencyService;
    private readonly IAttachmentTempStorageService _attachmentTempStorageService = attachmentTempStorageService;
    private readonly ILogger<BlockchainGrpcService> _logger = logger;

    /// <summary>FEAT-066: Maximum number of attachments per message.</summary>
    private const int MaxAttachmentsPerMessage = 5;

    /// <summary>FEAT-066: Maximum size per attachment blob (25MB).</summary>
    private const long MaxAttachmentSizeBytes = 25 * 1024 * 1024;

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

            // FEAT-066: Validate and save attachment blobs to temp storage
            var attachmentRefs = this.TryExtractAttachmentReferences(transaction);
            var savedAttachmentIds = new List<string>();

            if (request.Attachments.Count > 0 || (attachmentRefs != null && attachmentRefs.Count > 0))
            {
                var validationError = ValidateAttachmentBlobs(request, attachmentRefs);
                if (validationError != null)
                {
                    return new SubmitSignedTransactionReply
                    {
                        Successfull = false,
                        Message = validationError,
                        Status = TransactionStatus.Rejected
                    };
                }

                // Save blobs to temp storage
                foreach (var blob in request.Attachments)
                {
                    await this._attachmentTempStorageService.SaveAsync(
                        blob.AttachmentId,
                        blob.EncryptedOriginal.ToByteArray(),
                        blob.EncryptedThumbnail.Length > 0 ? blob.EncryptedThumbnail.ToByteArray() : null);
                    savedAttachmentIds.Add(blob.AttachmentId);
                }
            }

            foreach (var item in this._transactionContentHandlers)
            {
                if (item.CanValidate(transaction.PayloadKind))
                {
                    var transactionSignedByValidator = item.ValidateAndSign(transaction);

                    if (transactionSignedByValidator == null)
                    {
                        // FEAT-066: Clean up temp files on content validation rejection
                        foreach (var id in savedAttachmentIds)
                        {
                            await this._attachmentTempStorageService.DeleteAsync(id);
                        }

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

    /// <summary>
    /// FEAT-066: Extracts attachment references from a NewFeedMessagePayload transaction.
    /// Returns null for non-FeedMessage transactions or messages without attachments.
    /// </summary>
    private List<AttachmentReference>? TryExtractAttachmentReferences(AbstractTransaction transaction)
    {
        if (transaction is SignedTransaction<NewFeedMessagePayload> feedMessageTx)
        {
            return feedMessageTx.Payload.Attachments;
        }

        return null;
    }

    /// <summary>
    /// FEAT-066: Validates attachment blobs against metadata references in the transaction.
    /// Returns an error message if validation fails, or null if valid.
    /// </summary>
    internal static string? ValidateAttachmentBlobs(
        SubmitSignedTransactionRequest request,
        List<AttachmentReference>? attachmentRefs)
    {
        var blobCount = request.Attachments.Count;
        var refCount = attachmentRefs?.Count ?? 0;

        // Count check
        if (refCount > MaxAttachmentsPerMessage)
        {
            return $"Too many attachments: {refCount} exceeds the maximum of {MaxAttachmentsPerMessage}";
        }

        // If blobs present but no metadata references, or vice versa
        if (blobCount != refCount)
        {
            return $"Attachment blob count ({blobCount}) does not match metadata reference count ({refCount})";
        }

        // Size check
        foreach (var blob in request.Attachments)
        {
            var blobSize = blob.EncryptedOriginal.Length;
            if (blobSize > MaxAttachmentSizeBytes)
            {
                return $"Attachment '{blob.AttachmentId}' is {blobSize / (1024 * 1024)}MB, exceeds the maximum of 25MB";
            }
        }

        // ID matching check
        if (attachmentRefs != null)
        {
            var refIds = new HashSet<string>(attachmentRefs.Select(r => r.Id));
            var blobIds = new HashSet<string>(request.Attachments.Select(b => b.AttachmentId));

            if (!refIds.SetEquals(blobIds))
            {
                var missingBlobs = refIds.Except(blobIds).ToList();
                var extraBlobs = blobIds.Except(refIds).ToList();

                if (missingBlobs.Count > 0)
                    return $"Missing attachment blobs for metadata references: {string.Join(", ", missingBlobs)}";
                if (extraBlobs.Count > 0)
                    return $"Extra attachment blobs without metadata references: {string.Join(", ", extraBlobs)}";
            }
        }

        return null;
    }
}
