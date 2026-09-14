// EPIC-001 -> FEAT-011 Phase 3 Tasks 3.1/3.2.
// Malformed wire scalars must reach the existing typed RPC parsing rejection.
using System.Text.Json;
using System.Text.Json.Nodes;
using HushShared.Blockchain.Model;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Xunit;

namespace HushNode.Identity.Tests;

public sealed class FullIdentityScalarParsingTests
{
    [Theory]
    [InlineData("TransactionId", "not-a-uuid", false)]
    [InlineData("TransactionTimeStamp", "not-a-date", false)]
    [InlineData("TransactionId", "not-a-uuid", true)]
    [InlineData("TransactionTimeStamp", "not-a-date", true)]
    public void MalformedScalar_IsAParsingFailureHandledByIngress(string field, string value, bool validated)
    {
        var request = JsonNode.Parse(JsonSerializer.Serialize(FullIdentityTestData.BuildSigned()))!.AsObject();
        request[field] = value;
        var parser = new FullIdentityDeserializerStrategy();

        var failure = Assert.Throws<JsonException>(() => validated
            ? parser.DeserializeValidatedTransaction(request.ToJsonString())
            : parser.DeserializeSignedTransaction(request.ToJsonString()));

        // Keep raw attacker-supplied scalar values out of the boundary diagnostic.
        Assert.DoesNotContain(value, failure.Message);
        Assert.Null(failure.InnerException);
    }

    [Fact]
    public void ValidSignedAndValidatedEnvelopes_PreserveExactContent()
    {
        var signed = FullIdentityTestData.BuildSigned();
        var validated = new ValidatedTransaction<FullIdentityPayload>(signed, new SignatureInfo("public-validator", "public-fixture"));
        var parser = new FullIdentityDeserializerStrategy();

        Assert.Equal(signed, parser.DeserializeSignedTransaction(JsonSerializer.Serialize(signed)));
        Assert.Equal(validated, parser.DeserializeValidatedTransaction(JsonSerializer.Serialize(validated)));
    }
}
