using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class CloseElectionTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<CloseElectionTransactionHandler> logger) : ICloseElectionTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<CloseElectionTransactionHandler> _logger = logger;

    public async Task HandleCloseElectionTransaction(ValidatedTransaction<CloseElectionPayload> transaction)
    {
        var result = await _electionLifecycleService.CloseElectionAsync(new CloseElectionRequest(
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
                "[CloseElectionTransactionHandler] Failed to index close election transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
