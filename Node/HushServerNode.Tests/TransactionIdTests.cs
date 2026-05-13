using FluentAssertions;
using HushShared.Blockchain.TransactionModel;
using Xunit;

namespace HushServerNode.Tests;

public class TransactionIdTests
{
    [Fact]
    public void NewTransactionId_ReturnsFreshIdentifierOnEachAccess()
    {
        var first = TransactionId.NewTransactionId;
        var second = TransactionId.NewTransactionId;

        first.Should().NotBe(TransactionId.Empty);
        second.Should().NotBe(TransactionId.Empty);
        second.Should().NotBe(first);
    }
}
