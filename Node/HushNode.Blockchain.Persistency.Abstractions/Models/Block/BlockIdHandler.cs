namespace HushNode.Blockchain.Persistency.Abstractions.Models.Block;

public static class BlockIdHandler
{
    public static BlockId CreateFromString(string value) => new(Guid.Parse(value));
}
