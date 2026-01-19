using System.Reactive.Linq;
using FluentAssertions;
using HushServerNode.Testing;
using Xunit;

namespace HushServerNode.Tests;

/// <summary>
/// Unit tests for BlockProductionControl - verifies observable emission,
/// finalization signaling, and timeout behavior.
/// </summary>
public class BlockProductionControlTests
{
    #region Observable Emission Tests

    [Fact]
    public void TriggerBlockProduction_ShouldEmitValueToSubscriber()
    {
        // Arrange
        using var control = new BlockProductionControl();
        var receivedValues = new List<long>();
        using var subscription = control.Observable.Subscribe(v => receivedValues.Add(v));

        // Act
        control.TriggerBlockProduction();

        // Assert
        receivedValues.Should().HaveCount(1);
        receivedValues[0].Should().Be(1);
    }

    [Fact]
    public void TriggerBlockProduction_MultipleTimes_ShouldEmitValuesInOrder()
    {
        // Arrange
        using var control = new BlockProductionControl();
        var receivedValues = new List<long>();
        using var subscription = control.Observable.Subscribe(v => receivedValues.Add(v));

        // Act
        control.TriggerBlockProduction();
        control.TriggerBlockProduction();
        control.TriggerBlockProduction();

        // Assert
        receivedValues.Should().HaveCount(3);
        receivedValues.Should().BeEquivalentTo(new[] { 1L, 2L, 3L }, options => options.WithStrictOrdering());
    }

    [Fact]
    public void CreateObservableFactory_ShouldReturnFactoryThatReturnsObservable()
    {
        // Arrange
        using var control = new BlockProductionControl();
        var factory = control.CreateObservableFactory();
        var receivedValues = new List<long>();

        // Act
        var observable = factory();
        using var subscription = observable.Subscribe(v => receivedValues.Add(v));
        control.TriggerBlockProduction();

        // Assert
        receivedValues.Should().HaveCount(1);
    }

    #endregion

    #region ProduceBlockAsync and Finalization Tests

    [Fact]
    public async Task ProduceBlockAsync_WhenFinalizedImmediately_ShouldComplete()
    {
        // Arrange
        using var control = new BlockProductionControl();
        using var subscription = control.Observable.Subscribe(_ =>
        {
            // Simulate immediate block finalization
            control.OnBlockFinalized();
        });

        // Act
        var task = control.ProduceBlockAsync();

        // Assert
        await task;
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task ProduceBlockAsync_WhenFinalizedWithDelay_ShouldWaitForFinalization()
    {
        // Arrange
        using var control = new BlockProductionControl();
        var triggerReceived = false;
        using var subscription = control.Observable.Subscribe(_ =>
        {
            triggerReceived = true;
        });

        // Act
        var task = control.ProduceBlockAsync(TimeSpan.FromSeconds(5));

        // Give it a moment to start
        await Task.Delay(50);
        triggerReceived.Should().BeTrue();
        task.IsCompleted.Should().BeFalse();

        // Now finalize
        control.OnBlockFinalized();

        // Assert
        await task;
        task.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task ProduceBlockAsync_WhenNotFinalized_ShouldTimeout()
    {
        // Arrange
        using var control = new BlockProductionControl();
        using var subscription = control.Observable.Subscribe(_ =>
        {
            // Don't call OnBlockFinalized - simulate stuck block production
        });

        // Act & Assert
        var act = async () => await control.ProduceBlockAsync(TimeSpan.FromMilliseconds(100));
        await act.Should().ThrowAsync<TimeoutException>()
            .WithMessage("*did not complete within*");
    }

    [Fact]
    public async Task OnBlockFinalized_WithoutPendingTask_ShouldNotThrow()
    {
        // Arrange
        using var control = new BlockProductionControl();

        // Act
        var act = () => control.OnBlockFinalized();

        // Assert
        act.Should().NotThrow();
    }

    #endregion

    #region Disposal Tests

    [Fact]
    public void Dispose_ShouldCompleteObservable()
    {
        // Arrange
        var control = new BlockProductionControl();
        var completed = false;
        using var subscription = control.Observable.Subscribe(
            _ => { },
            () => completed = true);

        // Act
        control.Dispose();

        // Assert
        completed.Should().BeTrue();
    }

    [Fact]
    public void TriggerBlockProduction_AfterDispose_ShouldThrow()
    {
        // Arrange
        var control = new BlockProductionControl();
        control.Dispose();

        // Act
        var act = () => control.TriggerBlockProduction();

        // Assert
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public async Task ProduceBlockAsync_AfterDispose_ShouldThrow()
    {
        // Arrange
        var control = new BlockProductionControl();
        control.Dispose();

        // Act
        var act = async () => await control.ProduceBlockAsync();

        // Assert
        await act.Should().ThrowAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task ProduceBlockAsync_WhenDisposedWhileWaiting_ShouldCancel()
    {
        // Arrange
        var control = new BlockProductionControl();
        using var subscription = control.Observable.Subscribe(_ => { });

        // Act
        var task = control.ProduceBlockAsync(TimeSpan.FromSeconds(10));
        await Task.Delay(50); // Let it start waiting
        control.Dispose();

        // Assert
        var act = async () => await task;
        await act.Should().ThrowAsync<TaskCanceledException>();
    }

    #endregion

    #region Multiple Concurrent Calls Tests

    [Fact]
    public async Task ProduceBlockAsync_SequentialCalls_ShouldWorkCorrectly()
    {
        // Arrange
        using var control = new BlockProductionControl();
        var triggerCount = 0;
        using var subscription = control.Observable.Subscribe(_ =>
        {
            Interlocked.Increment(ref triggerCount);
            control.OnBlockFinalized();
        });

        // Act
        await control.ProduceBlockAsync();
        await control.ProduceBlockAsync();
        await control.ProduceBlockAsync();

        // Assert
        triggerCount.Should().Be(3);
    }

    #endregion
}
