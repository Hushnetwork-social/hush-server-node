namespace HushNode.Blockchain.Persistency.Abstractions.Model;

public static class BlockIndexHandler
{
    public static BlockIndex CreateNew(long value) => new(value);
}