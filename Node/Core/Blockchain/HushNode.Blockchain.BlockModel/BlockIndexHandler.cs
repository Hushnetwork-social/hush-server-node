namespace HushNode.Blockchain.Model.Block;

public static class BlockIndexHandler
{
    public static BlockIndex CreateNew(long value) => new(value);
}