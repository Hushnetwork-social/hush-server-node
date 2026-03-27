using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class FinalizeElectionTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<FinalizeElectionTransactionHandler> logger) : IFinalizeElectionTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<FinalizeElectionTransactionHandler> _logger = logger;

    public async Task HandleFinalizeElectionTransaction(ValidatedTransaction<FinalizeElectionPayload> transaction)
    {
        var result = await _electionLifecycleService.FinalizeElectionAsync(new FinalizeElectionRequest(
            ElectionId: transaction.Payload.ElectionId,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            AcceptedBallotSetHash: transaction.Payload.AcceptedBallotSetHash,
            FinalEncryptedTallyHash: transaction.Payload.FinalEncryptedTallyHash,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[FinalizeElectionTransactionHandler] Failed to index finalize election transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
