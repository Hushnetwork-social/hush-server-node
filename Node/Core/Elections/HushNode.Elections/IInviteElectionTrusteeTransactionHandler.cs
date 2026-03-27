using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IInviteElectionTrusteeTransactionHandler
{
    Task HandleInviteElectionTrusteeTransaction(ValidatedTransaction<InviteElectionTrusteePayload> transaction);
}
