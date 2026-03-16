using FluentAssertions;
using HushNode.Reactions.ZK;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

public sealed class SnarkJsVerificationKeyParserTests
{
    [Fact]
    public void Parse_WithSnarkJsStyleJson_MapsKeyMaterial()
    {
        var json = """
        {
          "protocol": "groth16",
          "curve": "bn128",
          "vk_alpha_1": ["11","12","1"],
          "vk_beta_2": [["21","22"],["23","24"],["1","0"]],
          "vk_gamma_2": [["31","32"],["33","34"],["1","0"]],
          "vk_delta_2": [["41","42"],["43","44"],["1","0"]],
          "IC": [
            ["51","52","1"],
            ["61","62","1"],
            ["71","72","1"]
          ]
        }
        """;

        var key = SnarkJsVerificationKeyParser.Parse(json, "omega-v1.0.0");

        key.Version.Should().Be("omega-v1.0.0");
        key.Alpha.X.ToString().Should().Be("11");
        key.Alpha.Y.ToString().Should().Be("12");
        key.Beta.Should().HaveCount(2);
        key.Beta[0].X.ToString().Should().Be("21");
        key.Beta[0].Y.ToString().Should().Be("22");
        key.Beta[1].X.ToString().Should().Be("23");
        key.Beta[1].Y.ToString().Should().Be("24");
        key.IC.Should().HaveCount(3);
        key.IC[2].X.ToString().Should().Be("71");
        key.IC[2].Y.ToString().Should().Be("72");
    }
}
