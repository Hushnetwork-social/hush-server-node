using HushShared.Blockchain.BlockModel;
using HushShared.Elections.Model;

namespace HushNode.Elections;

public interface IElectionBallotPublicationService
{
    Task ProcessPendingPublicationAsync(BlockIndex blockIndex);

    Task RepairClosedElectionResultsAsync(ElectionId electionId);
}
