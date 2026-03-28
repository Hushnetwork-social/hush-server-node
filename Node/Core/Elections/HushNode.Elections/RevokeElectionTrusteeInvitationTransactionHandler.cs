using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class RevokeElectionTrusteeInvitationTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<RevokeElectionTrusteeInvitationTransactionHandler> logger) : IRevokeElectionTrusteeInvitationTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<RevokeElectionTrusteeInvitationTransactionHandler> _logger = logger;

    public async Task HandleRevokeElectionTrusteeInvitationTransaction(ValidatedTransaction<RevokeElectionTrusteeInvitationPayload> transaction)
    {
        var result = await _electionLifecycleService.RevokeTrusteeInvitationAsync(new ResolveElectionTrusteeInvitationRequest(
            ElectionId: transaction.Payload.ElectionId,
            InvitationId: transaction.Payload.InvitationId,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[RevokeElectionTrusteeInvitationTransactionHandler] Failed to index trustee revoke transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
