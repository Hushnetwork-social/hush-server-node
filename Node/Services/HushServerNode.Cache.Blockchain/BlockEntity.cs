namespace HushServerNode.Cache.Blockchain;

public class BlockEntity
{
    public string BlockId { get; set; } = string.Empty;

    public long Height { get; set; }

    public string PreviousBlockId { get; set; } = string.Empty;

    public string NextBlockId { get; set; } = string.Empty;

    public string Hash { get; set; } = string.Empty;

    public string BlockJson { get; set; } = string.Empty;
}
