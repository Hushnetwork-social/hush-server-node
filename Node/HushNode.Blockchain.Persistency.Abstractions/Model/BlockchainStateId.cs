using System.Text.Json.Serialization;
using HushNode.Blockchain.Persistency.Abstractions.Converters;

namespace HushNode.Blockchain.Persistency.Abstractions.Model;

[JsonConverter(typeof(BlockchainStateIdConverter))]
public record BlockchainStateId(Guid Value)
{
    public static BlockchainStateId Empty { get; } = new(Guid.Empty);

    public static BlockchainStateId NewBlockchainStateId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}