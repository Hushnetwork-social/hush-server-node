using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class ApproveElectionGovernedProposalTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<ApproveElectionGovernedProposalTransactionHandler> logger) : IApproveElectionGovernedProposalTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<ApproveElectionGovernedProposalTransactionHandler> _logger = logger;

    public async Task HandleApproveElectionGovernedProposalTransaction(ValidatedTransaction<ApproveElectionGovernedProposalPayload> transaction)
    {
        var result = await _electionLifecycleService.ApproveGovernedProposalAsync(new ApproveElectionGovernedProposalRequest(
            ElectionId: transaction.Payload.ElectionId,
            ProposalId: transaction.Payload.ProposalId,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            ApprovalNote: transaction.Payload.ApprovalNote,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[ApproveElectionGovernedProposalTransactionHandler] Failed to index governed approval transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
