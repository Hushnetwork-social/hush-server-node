using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class RetryElectionGovernedProposalExecutionTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<RetryElectionGovernedProposalExecutionTransactionHandler> logger) : IRetryElectionGovernedProposalExecutionTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<RetryElectionGovernedProposalExecutionTransactionHandler> _logger = logger;

    public async Task HandleRetryElectionGovernedProposalExecutionTransaction(ValidatedTransaction<RetryElectionGovernedProposalExecutionPayload> transaction)
    {
        var result = await _electionLifecycleService.RetryGovernedProposalExecutionAsync(new RetryElectionGovernedProposalExecutionRequest(
            ElectionId: transaction.Payload.ElectionId,
            ProposalId: transaction.Payload.ProposalId,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[RetryElectionGovernedProposalExecutionTransactionHandler] Failed to index governed retry transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
