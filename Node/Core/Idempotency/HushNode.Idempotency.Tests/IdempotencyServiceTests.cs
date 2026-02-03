using FluentAssertions;
using HushNode.Feeds.Storage;
using HushNode.Interfaces.Models;
using HushShared.Feeds.Model;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using Olimpo.EntityFramework.Persistency;
using Xunit;

namespace HushNode.Idempotency.Tests;

/// <summary>
/// Unit tests for IdempotencyService.
/// FEAT-057: Server Message Idempotency.
/// Covers acceptance test scenarios F1-001, F1-002, F1-003.
/// Each test follows AAA pattern with isolated setup to prevent flaky tests.
/// </summary>
public class IdempotencyServiceTests
{
    private static FeedMessageId CreateMessageId() => new(Guid.NewGuid());

    #region CheckAsync Tests (F1-001, F1-002, F1-003)

    /// <summary>
    /// F1-001: New message with unique messageId is accepted.
    /// </summary>
    [Fact]
    public async Task CheckAsync_NewMessage_ReturnsAccepted()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ReturnsAsync(false);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(IdempotencyCheckResult.Accepted);
        mockRepository.Verify(x => x.ExistsByMessageIdAsync(messageId), Times.Once);
    }

    /// <summary>
    /// F1-002: Duplicate messageId found in database returns ALREADY_EXISTS.
    /// </summary>
    [Fact]
    public async Task CheckAsync_DuplicateInDatabase_ReturnsAlreadyExists()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ReturnsAsync(true); // Message exists in DB

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(IdempotencyCheckResult.AlreadyExists);
    }

    /// <summary>
    /// F1-003: Duplicate messageId found in MemPool returns PENDING.
    /// Database is NOT queried (early return).
    /// </summary>
    [Fact]
    public async Task CheckAsync_DuplicateInMemPool_ReturnsPending_DatabaseNotQueried()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // First, track the message in MemPool
        service.TryTrackInMemPool(messageId);

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(IdempotencyCheckResult.Pending);
        // Database should NOT be queried - verify it was never called
        mockRepository.Verify(x => x.ExistsByMessageIdAsync(It.IsAny<FeedMessageId>()), Times.Never);
    }

    /// <summary>
    /// Database error during check returns REJECTED (fail-closed).
    /// </summary>
    [Fact]
    public async Task CheckAsync_DatabaseError_ReturnsRejected()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ThrowsAsync(new Exception("Database connection failed"));

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = await service.CheckAsync(messageId);

        // Assert
        result.Should().Be(IdempotencyCheckResult.Rejected);
    }

    #endregion

    #region TryTrackInMemPool Tests

    [Fact]
    public void TryTrackInMemPool_FirstCall_ReturnsTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = service.TryTrackInMemPool(messageId);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void TryTrackInMemPool_SecondCall_ReturnsFalse()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // First call succeeds
        service.TryTrackInMemPool(messageId);

        // Act - Second call for same ID
        var result = service.TryTrackInMemPool(messageId);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void TryTrackInMemPool_DifferentIds_BothReturnTrue()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId1 = CreateMessageId();
        var messageId2 = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result1 = service.TryTrackInMemPool(messageId1);
        var result2 = service.TryTrackInMemPool(messageId2);

        // Assert
        result1.Should().BeTrue();
        result2.Should().BeTrue();
    }

    #endregion

    #region RemoveFromTracking Tests

    [Fact]
    public void RemoveFromTracking_RemovesTrackedMessages()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Track message first
        service.TryTrackInMemPool(messageId);

        // Act - Remove from tracking
        service.RemoveFromTracking(new[] { messageId });

        // Assert - Can track again (proves it was removed)
        var canTrackAgain = service.TryTrackInMemPool(messageId);
        canTrackAgain.Should().BeTrue();
    }

    [Fact]
    public void RemoveFromTracking_MultipleMessages_RemovesAll()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId1 = CreateMessageId();
        var messageId2 = CreateMessageId();
        var messageId3 = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Track all messages
        service.TryTrackInMemPool(messageId1);
        service.TryTrackInMemPool(messageId2);
        service.TryTrackInMemPool(messageId3);

        // Act - Remove all
        service.RemoveFromTracking(new[] { messageId1, messageId2, messageId3 });

        // Assert - All can be tracked again
        service.TryTrackInMemPool(messageId1).Should().BeTrue();
        service.TryTrackInMemPool(messageId2).Should().BeTrue();
        service.TryTrackInMemPool(messageId3).Should().BeTrue();
    }

    [Fact]
    public void RemoveFromTracking_NonExistentId_NoError()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act - Remove non-existent ID (should not throw)
        var act = () => service.RemoveFromTracking(new[] { messageId });

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RemoveFromTracking_EmptyCollection_NoError()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act - Remove empty collection (should not throw)
        var act = () => service.RemoveFromTracking(Array.Empty<FeedMessageId>());

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Concurrency Tests (Task 6.1)

    /// <summary>
    /// Verifies atomicity of TryTrackInMemPool under concurrent access.
    /// Only one of N parallel calls with the same messageId should succeed.
    /// </summary>
    [Fact]
    public void TryTrackInMemPool_ConcurrentCalls_OnlyOneSucceeds()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var service = mocker.CreateInstance<IdempotencyService>();

        const int parallelCount = 10;
        var results = new bool[parallelCount];

        // Act - 10 parallel calls with the same messageId
        Parallel.For(0, parallelCount, i =>
        {
            results[i] = service.TryTrackInMemPool(messageId);
        });

        // Assert
        var successCount = results.Count(r => r);
        var failCount = results.Count(r => !r);

        successCount.Should().Be(1, "exactly one call should succeed");
        failCount.Should().Be(parallelCount - 1, "all other calls should fail");
    }

    /// <summary>
    /// Verifies CheckAsync returns consistent PENDING results under concurrent access.
    /// </summary>
    [Fact]
    public async Task CheckAsync_ConcurrentCalls_ConsistentPendingResults()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(It.IsAny<FeedMessageId>()))
            .ReturnsAsync(false);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Track message first
        service.TryTrackInMemPool(messageId);

        const int parallelCount = 10;
        var tasks = new Task<IdempotencyCheckResult>[parallelCount];

        // Act - 10 parallel CheckAsync calls
        for (int i = 0; i < parallelCount; i++)
        {
            tasks[i] = service.CheckAsync(messageId);
        }

        var results = await Task.WhenAll(tasks);

        // Assert - All should return PENDING
        results.Should().AllBeEquivalentTo(IdempotencyCheckResult.Pending);

        // Database should NOT be queried (early return from MemPool check)
        mockRepository.Verify(x => x.ExistsByMessageIdAsync(It.IsAny<FeedMessageId>()), Times.Never);
    }

    /// <summary>
    /// Verifies RemoveFromTracking is thread-safe under concurrent access.
    /// </summary>
    [Fact]
    public void RemoveFromTracking_ConcurrentCalls_NoRaceConditions()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<IdempotencyService>();

        const int messageCount = 100;
        var messageIds = Enumerable.Range(0, messageCount)
            .Select(_ => CreateMessageId())
            .ToArray();

        // Track all messages
        foreach (var messageId in messageIds)
        {
            service.TryTrackInMemPool(messageId);
        }

        // Act - Remove from multiple threads concurrently
        var exceptions = new List<Exception>();
        Parallel.For(0, messageCount, i =>
        {
            try
            {
                // Each thread removes a different subset
                var subset = messageIds.Skip(i).Take(10).ToArray();
                service.RemoveFromTracking(subset);
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Assert - No exceptions
        exceptions.Should().BeEmpty("no exceptions should occur during concurrent cleanup");

        // All messages should be removed (can track them again)
        foreach (var messageId in messageIds)
        {
            service.TryTrackInMemPool(messageId).Should().BeTrue(
                $"messageId {messageId} should be trackable again after removal");
        }
    }

    /// <summary>
    /// Verifies concurrent tracking and removal operations don't cause race conditions.
    /// </summary>
    [Fact]
    public void ConcurrentTrackAndRemove_NoRaceConditions()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<IdempotencyService>();

        const int operationCount = 100;
        var messageIds = Enumerable.Range(0, operationCount)
            .Select(_ => CreateMessageId())
            .ToArray();

        var exceptions = new List<Exception>();

        // Act - Concurrent track and remove operations
        Parallel.Invoke(
            // Thread 1: Track messages
            () =>
            {
                try
                {
                    foreach (var messageId in messageIds.Take(50))
                    {
                        service.TryTrackInMemPool(messageId);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            },
            // Thread 2: Track more messages
            () =>
            {
                try
                {
                    foreach (var messageId in messageIds.Skip(25).Take(50))
                    {
                        service.TryTrackInMemPool(messageId);
                    }
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            },
            // Thread 3: Remove messages
            () =>
            {
                try
                {
                    service.RemoveFromTracking(messageIds.Take(25));
                }
                catch (Exception ex)
                {
                    lock (exceptions) { exceptions.Add(ex); }
                }
            }
        );

        // Assert - No exceptions
        exceptions.Should().BeEmpty("concurrent operations should not cause exceptions");
    }

    #endregion

    #region Edge Case Tests (Task 6.2)

    /// <summary>
    /// Verifies behavior with empty GUID message ID.
    /// Empty GUID should be handled like any other ID (tracked, checked, removed).
    /// </summary>
    [Fact]
    public async Task CheckAsync_EmptyGuid_HandledNormally()
    {
        // Arrange
        var mocker = new AutoMocker();
        var emptyGuidMessageId = new FeedMessageId(Guid.Empty);

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(emptyGuidMessageId))
            .ReturnsAsync(false);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var result = await service.CheckAsync(emptyGuidMessageId);

        // Assert - Empty GUID is valid and treated like any other ID
        result.Should().Be(IdempotencyCheckResult.Accepted);
    }

    /// <summary>
    /// Verifies TryTrackInMemPool with empty GUID.
    /// </summary>
    [Fact]
    public void TryTrackInMemPool_EmptyGuid_HandledNormally()
    {
        // Arrange
        var mocker = new AutoMocker();
        var emptyGuidMessageId = new FeedMessageId(Guid.Empty);
        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        var firstTrack = service.TryTrackInMemPool(emptyGuidMessageId);
        var secondTrack = service.TryTrackInMemPool(emptyGuidMessageId);

        // Assert - Empty GUID behaves like any other ID
        firstTrack.Should().BeTrue();
        secondTrack.Should().BeFalse();
    }

    /// <summary>
    /// Verifies large batch removal completes efficiently.
    /// 10,000 message IDs should be removed in reasonable time.
    /// </summary>
    [Fact]
    public void RemoveFromTracking_LargeBatch_CompletesEfficiently()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<IdempotencyService>();

        const int batchSize = 10_000;
        var messageIds = Enumerable.Range(0, batchSize)
            .Select(_ => CreateMessageId())
            .ToArray();

        // Track all messages
        foreach (var messageId in messageIds)
        {
            service.TryTrackInMemPool(messageId);
        }

        // Act - Time the removal
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        service.RemoveFromTracking(messageIds);
        stopwatch.Stop();

        // Assert - Should complete in less than 1 second
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000,
            $"removing {batchSize} items should complete quickly, took {stopwatch.ElapsedMilliseconds}ms");

        // Verify all were removed
        foreach (var messageId in messageIds.Take(10)) // Sample check
        {
            service.TryTrackInMemPool(messageId).Should().BeTrue(
                "messages should be trackable again after removal");
        }
    }

    /// <summary>
    /// Verifies partial batch removal - only existing IDs are removed, non-existent IDs are ignored.
    /// </summary>
    [Fact]
    public void RemoveFromTracking_MixedExistingAndNonExistent_OnlyRemovesExisting()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<IdempotencyService>();

        var trackedId = CreateMessageId();
        var notTrackedId = CreateMessageId();

        // Track only one ID
        service.TryTrackInMemPool(trackedId);

        // Act - Remove both (one exists, one doesn't)
        var act = () => service.RemoveFromTracking(new[] { trackedId, notTrackedId });

        // Assert - No error
        act.Should().NotThrow();

        // Tracked ID was removed (can track again)
        service.TryTrackInMemPool(trackedId).Should().BeTrue();
    }

    /// <summary>
    /// Verifies rapid sequential track/untrack cycles don't cause issues.
    /// </summary>
    [Fact]
    public void RapidTrackUntrackCycles_NoIssues()
    {
        // Arrange
        var mocker = new AutoMocker();
        var service = mocker.CreateInstance<IdempotencyService>();
        var messageId = CreateMessageId();

        // Act - Rapid track/untrack cycles
        for (int i = 0; i < 100; i++)
        {
            var tracked = service.TryTrackInMemPool(messageId);
            tracked.Should().BeTrue($"cycle {i}: should be able to track");

            service.RemoveFromTracking(new[] { messageId });
        }

        // Assert - Final state is untracked
        service.TryTrackInMemPool(messageId).Should().BeTrue("final track should succeed");
    }

    /// <summary>
    /// Verifies database recovery scenario - after REJECTED (database error),
    /// the same message can be submitted again when database recovers.
    /// </summary>
    [Fact]
    public async Task CheckAsync_DatabaseRecovery_AllowsNewSubmission()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var callCount = 0;
        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    throw new Exception("Database connection failed");
                }
                return false; // Second call succeeds, message not in DB
            });

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act - First call fails with REJECTED
        var firstResult = await service.CheckAsync(messageId);

        // Act - Second call (database "recovered")
        var secondResult = await service.CheckAsync(messageId);

        // Assert
        firstResult.Should().Be(IdempotencyCheckResult.Rejected);
        secondResult.Should().Be(IdempotencyCheckResult.Accepted);
    }

    /// <summary>
    /// Verifies multiple different message IDs can be tracked and checked independently.
    /// </summary>
    [Fact]
    public async Task MultipleMessageIds_IndependentTracking()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId1 = CreateMessageId();
        var messageId2 = CreateMessageId();
        var messageId3 = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(It.IsAny<FeedMessageId>()))
            .ReturnsAsync(false);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act - Track message 1 and 2, but not 3
        service.TryTrackInMemPool(messageId1);
        service.TryTrackInMemPool(messageId2);

        var result1 = await service.CheckAsync(messageId1);
        var result2 = await service.CheckAsync(messageId2);
        var result3 = await service.CheckAsync(messageId3);

        // Assert - 1 and 2 are in MemPool (PENDING), 3 is not (ACCEPTED)
        result1.Should().Be(IdempotencyCheckResult.Pending);
        result2.Should().Be(IdempotencyCheckResult.Pending);
        result3.Should().Be(IdempotencyCheckResult.Accepted);
    }

    #endregion

    #region Logging Verification Tests (Task 6.3)

    /// <summary>
    /// Verifies that ACCEPTED status is logged when a new message is accepted.
    /// </summary>
    [Fact]
    public async Task CheckAsync_Accepted_LogsDebugMessage()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ReturnsAsync(false);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var mockLogger = mocker.GetMock<ILogger<IdempotencyService>>();

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        await service.CheckAsync(messageId);

        // Assert - Verify debug logging was called (LogDebug uses Log with LogLevel.Debug)
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ACCEPTED")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that PENDING status is logged when a duplicate is found in MemPool.
    /// </summary>
    [Fact]
    public async Task CheckAsync_Pending_LogsDebugMessage()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var mockLogger = mocker.GetMock<ILogger<IdempotencyService>>();

        var service = mocker.CreateInstance<IdempotencyService>();

        // Track in MemPool first
        service.TryTrackInMemPool(messageId);

        // Act
        await service.CheckAsync(messageId);

        // Assert - Verify PENDING was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("PENDING")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that ALREADY_EXISTS status is logged when duplicate found in database.
    /// </summary>
    [Fact]
    public async Task CheckAsync_AlreadyExists_LogsDebugMessage()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ReturnsAsync(true);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var mockLogger = mocker.GetMock<ILogger<IdempotencyService>>();

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        await service.CheckAsync(messageId);

        // Assert - Verify ALREADY_EXISTS was logged
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ALREADY_EXISTS")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that database errors are logged at Error level with exception details.
    /// </summary>
    [Fact]
    public async Task CheckAsync_DatabaseError_LogsErrorWithException()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var expectedException = new Exception("Database connection failed");

        var mockRepository = new Mock<IFeedMessageRepository>();
        mockRepository
            .Setup(x => x.ExistsByMessageIdAsync(messageId))
            .ThrowsAsync(expectedException);

        var mockUnitOfWork = new Mock<IReadOnlyUnitOfWork<FeedsDbContext>>();
        mockUnitOfWork
            .Setup(x => x.GetRepository<IFeedMessageRepository>())
            .Returns(mockRepository.Object);

        mocker.GetMock<IUnitOfWorkProvider<FeedsDbContext>>()
            .Setup(x => x.CreateReadOnly())
            .Returns(mockUnitOfWork.Object);

        var mockLogger = mocker.GetMock<ILogger<IdempotencyService>>();

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        await service.CheckAsync(messageId);

        // Assert - Verify Error was logged with the exception
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("REJECTED")),
                expectedException,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that TryTrackInMemPool logs when adding a new message.
    /// </summary>
    [Fact]
    public void TryTrackInMemPool_NewMessage_LogsDebugMessage()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var mockLogger = mocker.GetMock<ILogger<IdempotencyService>>();

        var service = mocker.CreateInstance<IdempotencyService>();

        // Act
        service.TryTrackInMemPool(messageId);

        // Assert - Verify debug logging for tracking
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("added to MemPool tracking")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    /// <summary>
    /// Verifies that RemoveFromTracking logs when removing messages.
    /// </summary>
    [Fact]
    public void RemoveFromTracking_WithMessages_LogsDebugMessage()
    {
        // Arrange
        var mocker = new AutoMocker();
        var messageId = CreateMessageId();
        var mockLogger = mocker.GetMock<ILogger<IdempotencyService>>();

        var service = mocker.CreateInstance<IdempotencyService>();

        // Track first, then remove
        service.TryTrackInMemPool(messageId);

        // Act
        service.RemoveFromTracking(new[] { messageId });

        // Assert - Verify debug logging for removal
        mockLogger.Verify(
            x => x.Log(
                LogLevel.Debug,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("Removed") && o.ToString()!.Contains("message(s)")),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    #endregion
}
