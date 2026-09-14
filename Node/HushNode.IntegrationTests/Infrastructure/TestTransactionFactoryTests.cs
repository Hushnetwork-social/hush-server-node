using System.Text.Json;
using FluentAssertions;
using HushNode.Identity;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using HushShared.Blockchain.TransactionModel;
using HushShared.Blockchain.TransactionModel.States;
using HushShared.Identity.Model;
using Xunit;

namespace HushNode.IntegrationTests.Tests;

public class TestTransactionFactoryTests
{
    [Theory]
    [InlineData("Alice")]
    [InlineData("Élection 🌍")]
    public void IdentityRegistrationSatisfiesTheProductionAdmissionContract(string alias)
    {
        var identity = TestIdentities.GenerateFromSeed("identity-factory-contract", alias);
        var json = TestTransactionFactory.CreateIdentityRegistration(identity);
        var transaction = JsonSerializer.Deserialize<SignedTransaction<FullIdentityPayload>>(json)!;
        var validator = new FullIdentityValidator(new FullIdentityCanonicalSerializer(), new FullIdentitySignatureVerifier());

        var result = validator.Validate(transaction);
        result.IsValid.Should().BeTrue("the fixture must satisfy admission ({0})", result.ValidationCode);
    }
}
