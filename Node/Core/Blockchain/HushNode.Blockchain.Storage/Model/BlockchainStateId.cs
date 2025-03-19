using System.Text.Json.Serialization;
using HushNode.Blockchain.Storage.Converters;

namespace HushNode.Blockchain.Storage.Model;

[JsonConverter(typeof(BlockchainStateIdConverter))]
public record BlockchainStateId(Guid Value)
{
    public static BlockchainStateId Empty { get; } = new(Guid.Empty);

    public static BlockchainStateId NewBlockchainStateId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}