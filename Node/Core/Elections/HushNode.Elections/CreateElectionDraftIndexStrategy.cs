using HushNode.Indexing.Interfaces;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public class CreateElectionDraftIndexStrategy(
    ICreateElectionDraftTransactionHandler createElectionDraftTransactionHandler) : IIndexStrategy
{
    private readonly ICreateElectionDraftTransactionHandler _createElectionDraftTransactionHandler = createElectionDraftTransactionHandler;

    public bool CanHandle(AbstractTransaction transaction) =>
        CreateElectionDraftPayloadHandler.CreateElectionDraftPayloadKind == transaction.PayloadKind;

    public async Task HandleAsync(AbstractTransaction transaction) =>
        await _createElectionDraftTransactionHandler.HandleCreateElectionDraftTransaction(
            (ValidatedTransaction<CreateElectionDraftPayload>)transaction);
}
