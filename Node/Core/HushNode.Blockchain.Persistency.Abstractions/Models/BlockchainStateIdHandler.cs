namespace HushNode.Blockchain.Persistency.Abstractions.Models;

public static class BlockchainStateIdHandler
{
    public static BlockchainStateId CreateFromString(string value) => new(Guid.Parse(value));
}
