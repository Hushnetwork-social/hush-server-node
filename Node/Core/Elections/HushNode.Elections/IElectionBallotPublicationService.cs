using HushShared.Blockchain.BlockModel;

namespace HushNode.Elections;

public interface IElectionBallotPublicationService
{
    Task ProcessPendingPublicationAsync(BlockIndex blockIndex);
}
