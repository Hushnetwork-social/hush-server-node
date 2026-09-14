// EPIC-001 -> FEAT-011 Phase 3 Tasks 3.1/3.2.
// Unsupported wire kinds must reach the RPC's typed malformed-input boundary.
using System.Text.Json;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Xunit;

namespace HushNode.Identity.Tests;

public sealed class TransactionIngressParsingTests
{
    public TransactionIngressParsingTests() =>
        _ = new TransactionDeserializerHandler([new FullIdentityDeserializerStrategy()]);

    [Fact]
    public void UnsupportedWireKind_IsAParsingFailureHandledByIngress()
    {
        const string request = "{\"PayloadKind\":\"ffffffff-ffff-ffff-ffff-ffffffffffff\"}";
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AbstractTransaction>(request));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"PayloadKind\":42}")]
    [InlineData("{\"PayloadKind\":{}}")]
    [InlineData("{\"PayloadKind\":[]}")]
    [InlineData("[]")]
    [InlineData("42")]
    [InlineData("\"transaction\"")]
    [InlineData("true")]
    public void MalformedRootOrKind_IsAParsingFailureHandledByIngress(string request)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AbstractTransaction>(request));
    }

    [Fact]
    public void RegisteredIdentityKind_StillDeserializesTheExactSignedTransaction()
    {
        var expected = FullIdentityTestData.BuildSigned();
        var actual = Assert.IsType<SignedTransaction<FullIdentityPayload>>(
            JsonSerializer.Deserialize<AbstractTransaction>(JsonSerializer.Serialize(expected)));
        Assert.Equal(expected, actual);
    }
}
