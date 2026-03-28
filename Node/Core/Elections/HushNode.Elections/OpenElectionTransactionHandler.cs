using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class OpenElectionTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<OpenElectionTransactionHandler> logger) : IOpenElectionTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<OpenElectionTransactionHandler> _logger = logger;

    public async Task HandleOpenElectionTransaction(ValidatedTransaction<OpenElectionPayload> transaction)
    {
        var result = await _electionLifecycleService.OpenElectionAsync(new OpenElectionRequest(
            ElectionId: transaction.Payload.ElectionId,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            RequiredWarningCodes: transaction.Payload.RequiredWarningCodes,
            FrozenEligibleVoterSetHash: transaction.Payload.FrozenEligibleVoterSetHash,
            TrusteePolicyExecutionReference: transaction.Payload.TrusteePolicyExecutionReference,
            ReportingPolicyExecutionReference: transaction.Payload.ReportingPolicyExecutionReference,
            ReviewWindowExecutionReference: transaction.Payload.ReviewWindowExecutionReference,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[OpenElectionTransactionHandler] Failed to index open election transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
