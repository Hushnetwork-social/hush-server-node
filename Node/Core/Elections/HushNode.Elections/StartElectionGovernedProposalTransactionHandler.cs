using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class StartElectionGovernedProposalTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<StartElectionGovernedProposalTransactionHandler> logger) : IStartElectionGovernedProposalTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<StartElectionGovernedProposalTransactionHandler> _logger = logger;

    public async Task HandleStartElectionGovernedProposalTransaction(ValidatedTransaction<StartElectionGovernedProposalPayload> transaction)
    {
        var result = await _electionLifecycleService.StartGovernedProposalAsync(new StartElectionGovernedProposalRequest(
            ElectionId: transaction.Payload.ElectionId,
            ActionType: transaction.Payload.ActionType,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            PreassignedProposalId: transaction.Payload.ProposalId,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[StartElectionGovernedProposalTransactionHandler] Failed to index governed proposal transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
