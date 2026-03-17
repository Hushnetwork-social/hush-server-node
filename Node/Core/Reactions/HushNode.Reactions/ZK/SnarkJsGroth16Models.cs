using System.Text.Json.Serialization;

namespace HushNode.Reactions.ZK;

public sealed record SnarkJsGroth16Proof(
    [property: JsonPropertyName("pi_a")]
    string[] PiA,
    [property: JsonPropertyName("pi_b")]
    string[][] PiB,
    [property: JsonPropertyName("pi_c")]
    string[] PiC,
    [property: JsonPropertyName("protocol")]
    string Protocol,
    [property: JsonPropertyName("curve")]
    string Curve);
