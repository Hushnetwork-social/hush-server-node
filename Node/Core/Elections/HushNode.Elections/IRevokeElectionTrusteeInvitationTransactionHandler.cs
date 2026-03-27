using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IRevokeElectionTrusteeInvitationTransactionHandler
{
    Task HandleRevokeElectionTrusteeInvitationTransaction(ValidatedTransaction<RevokeElectionTrusteeInvitationPayload> transaction);
}
