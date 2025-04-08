using System.Text.Json.Serialization;
using HushShared.Blockchain.BlockModel.Converters;

namespace HushShared.Blockchain.BlockModel;

[JsonConverter(typeof(BlockchainStateIdConverter))]
public record BlockchainStateId(Guid Value)
{
    public static BlockchainStateId Empty { get; } = new(Guid.Empty);

    public static BlockchainStateId NewBlockchainStateId { get; } = new(Guid.NewGuid());

    public override string ToString() => Value.ToString();
}