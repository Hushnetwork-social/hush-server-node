namespace HushNode.Blockchain.Model.Block;

public static class BlockIdHandler
{
    public static BlockId CreateFromString(string value) => new(Guid.Parse(value));
}
