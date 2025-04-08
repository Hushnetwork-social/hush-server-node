namespace HushShared.Blockchain.BlockModel;

public static class BlockchainStateIdHandler
{
    public static BlockchainStateId CreateFromString(string value) => new(Guid.Parse(value));
}
