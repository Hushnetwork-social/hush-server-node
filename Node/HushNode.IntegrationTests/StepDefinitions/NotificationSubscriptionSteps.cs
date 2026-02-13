using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushServerNode.Testing;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-063: Step definitions for gRPC notification stream testing.
/// Manages stream subscriptions via NotificationStreamHelper and provides
/// assertion steps for received events.
///
/// Reusable across FEAT-063, CF-005, CE-001 and future EPIC-005 features.
/// </summary>
[Binding]
public sealed class NotificationSubscriptionSteps
{
    private const string StreamHelperKeyPrefix = "NotificationStreamHelper_";
    private const string LastReceivedEventKey = "LastReceivedFeedEvent";

    private readonly ScenarioContext _scenarioContext;

    public NotificationSubscriptionSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"""(.*)"" subscribes to notification events via gRPC")]
    [When(@"""(.*)"" subscribes to notification events via gRPC")]
    public async Task GivenUserSubscribesToNotificationEventsViaGrpc(string userName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var notificationClient = grpcFactory.CreateClient<HushNotification.HushNotificationClient>();

        var helper = new NotificationStreamHelper(notificationClient);
        await helper.StartAsync(identity.PublicSigningAddress);

        // Store helper in context for later assertions and cleanup
        _scenarioContext[$"{StreamHelperKeyPrefix}{userName}"] = helper;

        // Give the stream a moment to connect and receive the initial UNREAD_COUNT_SYNC
        await Task.Delay(500);
    }

    [When(@"(.*) marks the feed as read up to block (.*)")]
    public async Task WhenUserMarksTheFeedAsReadUpToBlock(string userName, long blockIndex)
    {
        var identity = GetTestIdentity(userName);
        var feedId = GetChatFeedId();
        var grpcFactory = GetGrpcFactory();
        var notificationClient = grpcFactory.CreateClient<HushNotification.HushNotificationClient>();

        var response = await notificationClient.MarkFeedAsReadAsync(new MarkFeedAsReadRequest
        {
            UserId = identity.PublicSigningAddress,
            FeedId = feedId.ToString(),
            UpToBlockIndex = blockIndex
        });

        response.Success.Should().BeTrue("MarkFeedAsRead should succeed");

        _scenarioContext["LastMarkAsReadFeedId"] = feedId.ToString();
        _scenarioContext["LastMarkAsReadBlockIndex"] = blockIndex;
    }

    [Then(@"""(.*)"" receives a MessagesRead event within (.*)ms")]
    public async Task ThenUserReceivesAMessagesReadEventWithinMs(string userName, int timeoutMs)
    {
        var helper = GetStreamHelper(userName);

        var evt = await helper.WaitForEventAsync(
            e => e.Type == EventType.MessagesRead,
            TimeSpan.FromMilliseconds(timeoutMs));

        evt.Should().NotBeNull("Should receive a MessagesRead event");
        _scenarioContext[LastReceivedEventKey] = evt;
    }

    [Then(@"the event contains the correct feedId")]
    public void ThenTheEventContainsTheCorrectFeedId()
    {
        var evt = GetLastReceivedEvent();
        var expectedFeedId = (string)_scenarioContext["LastMarkAsReadFeedId"];

        evt.FeedId.Should().Be(expectedFeedId, "Event feedId should match the feed that was marked as read");
    }

    [Then(@"the event contains upToBlockIndex = (.*)")]
    public void ThenTheEventContainsUpToBlockIndex(long expectedBlockIndex)
    {
        var evt = GetLastReceivedEvent();
        evt.UpToBlockIndex.Should().Be(expectedBlockIndex,
            $"Event upToBlockIndex should be {expectedBlockIndex}");
    }

    [Then(@"""(.*)"" receives UNREAD_COUNT_SYNC on first connection")]
    public async Task ThenUserReceivesUnreadCountSyncOnFirstConnection(string userName)
    {
        var helper = GetStreamHelper(userName);

        var evt = await helper.WaitForEventAsync(
            e => e.Type == EventType.UnreadCountSync,
            TimeSpan.FromSeconds(5));

        evt.Should().NotBeNull("Should receive an UNREAD_COUNT_SYNC event on first connection");
        _scenarioContext[LastReceivedEventKey] = evt;
    }

    /// <summary>
    /// AfterScenario hook to dispose all stream helpers (prevent resource leaks).
    /// </summary>
    [AfterScenario]
    public async Task DisposeStreamHelpers()
    {
        foreach (var key in _scenarioContext.Keys.Where(k => k.StartsWith(StreamHelperKeyPrefix)))
        {
            if (_scenarioContext[key] is NotificationStreamHelper helper)
            {
                await helper.DisposeAsync();
            }
        }
    }

    #region Helper Methods

    private NotificationStreamHelper GetStreamHelper(string userName)
    {
        var key = $"{StreamHelperKeyPrefix}{userName}";
        if (_scenarioContext.TryGetValue(key, out var helperObj) && helperObj is NotificationStreamHelper helper)
        {
            return helper;
        }
        throw new InvalidOperationException(
            $"No notification stream helper found for {userName}. " +
            "Ensure the user has subscribed to events first.");
    }

    private FeedEvent GetLastReceivedEvent()
    {
        if (_scenarioContext.TryGetValue(LastReceivedEventKey, out var eventObj) && eventObj is FeedEvent evt)
        {
            return evt;
        }
        throw new InvalidOperationException("No received event found in context. Ensure an event assertion step has run.");
    }

    private HushShared.Feeds.Model.FeedId GetChatFeedId()
    {
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeed_") && !key.Contains("AesKey"))
            {
                return (HushShared.Feeds.Model.FeedId)_scenarioContext[key];
            }
        }
        throw new InvalidOperationException("No chat feed found in context");
    }

    private TestIdentity GetTestIdentity(string userName)
    {
        var contextKey = $"Identity_{userName}";
        if (_scenarioContext.TryGetValue(contextKey, out var identityObj) && identityObj is TestIdentity identity)
        {
            return identity;
        }

        return userName.ToLowerInvariant() switch
        {
            "alice" => TestIdentities.Alice,
            "bob" => TestIdentities.Bob,
            "charlie" => TestIdentities.Charlie,
            _ => throw new ArgumentException($"Unknown test user: {userName}")
        };
    }

    private GrpcClientFactory GetGrpcFactory()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.GrpcFactoryKey, out var factoryObj)
            && factoryObj is GrpcClientFactory grpcFactory)
        {
            return grpcFactory;
        }
        throw new InvalidOperationException("GrpcClientFactory not found in ScenarioContext.");
    }

    #endregion
}
