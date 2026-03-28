using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class CreateElectionDraftTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<CreateElectionDraftTransactionHandler> logger) : ICreateElectionDraftTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<CreateElectionDraftTransactionHandler> _logger = logger;

    public async Task HandleCreateElectionDraftTransaction(ValidatedTransaction<CreateElectionDraftPayload> transaction)
    {
        var result = await _electionLifecycleService.CreateDraftAsync(new CreateElectionDraftRequest(
            OwnerPublicAddress: transaction.Payload.OwnerPublicAddress,
            ActorPublicAddress: transaction.UserSignature.Signatory,
            SnapshotReason: transaction.Payload.SnapshotReason,
            Draft: transaction.Payload.Draft,
            PreassignedElectionId: transaction.Payload.ElectionId,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[CreateElectionDraftTransactionHandler] Failed to index election create transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
