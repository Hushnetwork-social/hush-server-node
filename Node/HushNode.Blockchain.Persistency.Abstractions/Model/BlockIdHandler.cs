namespace HushNode.Blockchain.Persistency.Abstractions.Model;

public static class BlockIdHandler
{
    public static BlockId CreateFromString(string value) => new(Guid.Parse(value));
}
