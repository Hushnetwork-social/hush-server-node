namespace HushNode.Blockchain.Persistency.Abstractions.Models.Block;

public static class BlockIndexHandler
{
    public static BlockIndex CreateNew(long value) => new(value);
}