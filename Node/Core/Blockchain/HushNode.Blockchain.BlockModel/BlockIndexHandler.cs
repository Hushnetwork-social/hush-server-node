namespace HushNode.Blockchain.BlockModel;

public static class BlockIndexHandler
{
    public static BlockIndex CreateNew(long value) => new(value);
}