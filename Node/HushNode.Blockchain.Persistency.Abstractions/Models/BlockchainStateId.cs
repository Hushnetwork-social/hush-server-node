using System.Text.Json.Serialization;
using HushNode.Blockchain.Persistency.Abstractions.Models.Converters;

namespace HushNode.Blockchain.Persistency.Abstractions.Models;

[JsonConverter(typeof(BlockchainStateIdConverter))]
public record BlockchainStateId(Guid Value)
{
    public static BlockchainStateId Empty { get; } = new(Guid.Empty);

    public static BlockchainStateId NewBlockchainStateId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}