using HushNode.Caching;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;
using Microsoft.Extensions.Logging;

namespace HushNode.Elections;

public class InviteElectionTrusteeTransactionHandler(
    IElectionLifecycleService electionLifecycleService,
    IBlockchainCache blockchainCache,
    ILogger<InviteElectionTrusteeTransactionHandler> logger) : IInviteElectionTrusteeTransactionHandler
{
    private readonly IElectionLifecycleService _electionLifecycleService = electionLifecycleService;
    private readonly IBlockchainCache _blockchainCache = blockchainCache;
    private readonly ILogger<InviteElectionTrusteeTransactionHandler> _logger = logger;

    public async Task HandleInviteElectionTrusteeTransaction(ValidatedTransaction<InviteElectionTrusteePayload> transaction)
    {
        var result = await _electionLifecycleService.InviteTrusteeAsync(new InviteElectionTrusteeRequest(
            ElectionId: transaction.Payload.ElectionId,
            ActorPublicAddress: transaction.Payload.ActorPublicAddress,
            TrusteeUserAddress: transaction.Payload.TrusteeUserAddress,
            TrusteeDisplayName: transaction.Payload.TrusteeDisplayName,
            PreassignedInvitationId: transaction.Payload.InvitationId,
            SourceTransactionId: transaction.TransactionId.Value,
            SourceBlockHeight: _blockchainCache.LastBlockIndex.Value,
            SourceBlockId: _blockchainCache.CurrentBlockId.Value));

        if (!result.IsSuccess)
        {
            _logger.LogWarning(
                "[InviteElectionTrusteeTransactionHandler] Failed to index trustee invitation transaction {TransactionId}: {ErrorCode} {ErrorMessage}",
                transaction.TransactionId,
                result.ErrorCode,
                result.ErrorMessage);
        }
    }
}
