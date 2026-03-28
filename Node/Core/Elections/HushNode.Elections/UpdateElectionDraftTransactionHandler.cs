using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class UpdateElectionDraftTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<UpdateElectionDraftTransactionHandler> logger) : IUpdateElectionDraftTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<UpdateElectionDraftTransactionHandler> _logger = logger;

    public async Task HandleUpdateElectionDraftTransaction(ValidatedTransaction<UpdateElectionDraftPayload> transaction)
    {
        var result = await _electionLifecycleService.UpdateDraftAsync(new UpdateElectionDraftRequest(
            ElectionId: transaction.Payload.ElectionId,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            SnapshotReason: transaction.Payload.SnapshotReason,
            Draft: transaction.Payload.Draft,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[UpdateElectionDraftTransactionHandler] Failed to index election update transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
