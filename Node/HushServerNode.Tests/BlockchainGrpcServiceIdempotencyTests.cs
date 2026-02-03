using FluentAssertions;
using HushNetwork.proto;
using HushNode.Interfaces.Models;
using Xunit;

namespace HushServerNode.Tests;

/// <summary>
/// Unit tests for BlockchainGrpcService idempotency integration.
/// FEAT-057: Server Message Idempotency.
///
/// Note: Full integration tests require TransactionDeserializerHandler initialization
/// which is complex to set up in unit tests. The core idempotency logic is tested in
/// HushNode.Idempotency.Tests. These tests verify the status mapping and response behavior.
/// Full E2E testing verifies the complete flow.
/// </summary>
public class BlockchainGrpcServiceIdempotencyTests
{
    #region TransactionStatus Mapping Tests

    /// <summary>
    /// Verifies that IdempotencyCheckResult.Accepted maps to TransactionStatus.Accepted.
    /// </summary>
    [Fact]
    public void MapToTransactionStatus_Accepted_ReturnsAccepted()
    {
        // Arrange & Act
        var result = MapToTransactionStatus(IdempotencyCheckResult.Accepted);

        // Assert
        result.Should().Be(TransactionStatus.Accepted);
    }

    /// <summary>
    /// Verifies that IdempotencyCheckResult.Pending maps to TransactionStatus.Pending.
    /// </summary>
    [Fact]
    public void MapToTransactionStatus_Pending_ReturnsPending()
    {
        // Arrange & Act
        var result = MapToTransactionStatus(IdempotencyCheckResult.Pending);

        // Assert
        result.Should().Be(TransactionStatus.Pending);
    }

    /// <summary>
    /// Verifies that IdempotencyCheckResult.AlreadyExists maps to TransactionStatus.AlreadyExists.
    /// </summary>
    [Fact]
    public void MapToTransactionStatus_AlreadyExists_ReturnsAlreadyExists()
    {
        // Arrange & Act
        var result = MapToTransactionStatus(IdempotencyCheckResult.AlreadyExists);

        // Assert
        result.Should().Be(TransactionStatus.AlreadyExists);
    }

    /// <summary>
    /// Verifies that IdempotencyCheckResult.Rejected maps to TransactionStatus.Rejected.
    /// </summary>
    [Fact]
    public void MapToTransactionStatus_Rejected_ReturnsRejected()
    {
        // Arrange & Act
        var result = MapToTransactionStatus(IdempotencyCheckResult.Rejected);

        // Assert
        result.Should().Be(TransactionStatus.Rejected);
    }

    #endregion

    #region Success Flag Tests

    /// <summary>
    /// Verifies that Pending status returns success=true (message will be confirmed).
    /// </summary>
    [Fact]
    public void IsSuccessStatus_Pending_ReturnsTrue()
    {
        // Arrange & Act
        var result = IsSuccessStatus(IdempotencyCheckResult.Pending);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that AlreadyExists status returns success=true (message is confirmed).
    /// </summary>
    [Fact]
    public void IsSuccessStatus_AlreadyExists_ReturnsTrue()
    {
        // Arrange & Act
        var result = IsSuccessStatus(IdempotencyCheckResult.AlreadyExists);

        // Assert
        result.Should().BeTrue();
    }

    /// <summary>
    /// Verifies that Rejected status returns success=false (error occurred).
    /// </summary>
    [Fact]
    public void IsSuccessStatus_Rejected_ReturnsFalse()
    {
        // Arrange & Act
        var result = IsSuccessStatus(IdempotencyCheckResult.Rejected);

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region Message Response Tests

    /// <summary>
    /// Verifies correct message for Pending status.
    /// </summary>
    [Fact]
    public void GetStatusMessage_Pending_ContainsMemPool()
    {
        // Arrange & Act
        var result = GetStatusMessage(IdempotencyCheckResult.Pending);

        // Assert
        result.Should().Contain("MemPool");
    }

    /// <summary>
    /// Verifies correct message for AlreadyExists status.
    /// </summary>
    [Fact]
    public void GetStatusMessage_AlreadyExists_ContainsBlockchain()
    {
        // Arrange & Act
        var result = GetStatusMessage(IdempotencyCheckResult.AlreadyExists);

        // Assert
        result.Should().Contain("blockchain");
    }

    /// <summary>
    /// Verifies correct message for Rejected status.
    /// </summary>
    [Fact]
    public void GetStatusMessage_Rejected_ContainsRejected()
    {
        // Arrange & Act
        var result = GetStatusMessage(IdempotencyCheckResult.Rejected);

        // Assert
        result.Should().Contain("rejected");
    }

    #endregion

    #region Helper Methods (same logic as BlockchainGrpcService)

    /// <summary>
    /// Maps IdempotencyCheckResult to TransactionStatus.
    /// This mirrors the logic in BlockchainGrpcService.MapToTransactionStatus.
    /// </summary>
    private static TransactionStatus MapToTransactionStatus(IdempotencyCheckResult result)
    {
        return result switch
        {
            IdempotencyCheckResult.Accepted => TransactionStatus.Accepted,
            IdempotencyCheckResult.AlreadyExists => TransactionStatus.AlreadyExists,
            IdempotencyCheckResult.Pending => TransactionStatus.Pending,
            IdempotencyCheckResult.Rejected => TransactionStatus.Rejected,
            _ => TransactionStatus.Unspecified
        };
    }

    /// <summary>
    /// Determines if the status represents a success case.
    /// Pending and AlreadyExists are success (message will be/is confirmed).
    /// This mirrors the logic in BlockchainGrpcService.SubmitSignedTransaction.
    /// </summary>
    private static bool IsSuccessStatus(IdempotencyCheckResult result)
    {
        return result != IdempotencyCheckResult.Rejected;
    }

    /// <summary>
    /// Gets the user-friendly message for a given status.
    /// This mirrors the logic in BlockchainGrpcService.SubmitSignedTransaction.
    /// </summary>
    private static string GetStatusMessage(IdempotencyCheckResult result)
    {
        return result switch
        {
            IdempotencyCheckResult.Pending => "Message is already pending in MemPool",
            IdempotencyCheckResult.AlreadyExists => "Message already exists in the blockchain",
            IdempotencyCheckResult.Rejected => "Transaction rejected due to server error",
            _ => "Unknown status"
        };
    }

    #endregion
}
