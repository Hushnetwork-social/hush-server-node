namespace HushServerNode.InternalModule.Blockchain.Cache;

public class BlockchainState
{
    public Guid BlockchainStateId { get; set; }

    public long LastBlockIndex { get; set; }    

    public string CurrentBlockId { get; set; } = string.Empty;

    public string CurrentPreviousBlockId { get; set; } = string.Empty;

    public string CurrentNextBlockId { get; set; } = string.Empty;
}
