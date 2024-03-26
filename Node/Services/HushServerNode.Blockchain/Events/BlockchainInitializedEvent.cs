namespace HushServerNode.Blockchain.Events;

public class BlockchainInitializedEvent 
{ 
    public string CurrentBlockId { get; } = string.Empty;
    public double CurrentBlockIndex { get; }
    public string NextBlockId { get; } = string.Empty;

    public BlockchainInitializedEvent(string currentBlockId, string nextBlockId, double CurrentBlockIndex)
    {
        this.CurrentBlockId = currentBlockId;
        this.NextBlockId = nextBlockId;
        this.CurrentBlockIndex = CurrentBlockIndex;
    }
}
