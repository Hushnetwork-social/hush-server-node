using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class RevokeElectionTrusteeInvitationIndexStrategy(
    IRevokeElectionTrusteeInvitationTransactionHandler transactionHandler) : IIndexStrategy
{
    private readonly IRevokeElectionTrusteeInvitationTransactionHandler _transactionHandler = transactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        RevokeElectionTrusteeInvitationPayloadHandler.RevokeElectionTrusteeInvitationPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _transactionHandler.HandleRevokeElectionTrusteeInvitationTransaction(
            (ValidatedTransaction<RevokeElectionTrusteeInvitationPayload>)transaction);
}
