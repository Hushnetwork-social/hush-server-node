using FluentAssertions;
using HushNetwork.proto;
using HushNode.IntegrationTests.Hooks;
using HushNode.IntegrationTests.Infrastructure;
using HushShared.Feeds.Model;
using HushServerNode.Testing;
using StackExchange.Redis;
using TechTalk.SpecFlow;

namespace HushNode.IntegrationTests.StepDefinitions;

/// <summary>
/// FEAT-050: Step definitions for Feed Participants & Group Keys Cache scenarios.
/// Tests cache-aside pattern for:
/// - Participants: Populated by NotificationEventHandler when messages are sent (Redis SET)
/// - Key Generations: Populated by GetKeyGenerations gRPC on lookup (JSON STRING)
/// </summary>
[Binding]
public sealed class FeedParticipantsCacheSteps
{
    private readonly ScenarioContext _scenarioContext;

    public FeedParticipantsCacheSteps(ScenarioContext scenarioContext)
    {
        _scenarioContext = scenarioContext;
    }

    [Given(@"(.*) has created a group feed ""(.*)""")]
    public async Task GivenUserHasCreatedAGroupFeed(string userName, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateGroupFeed(identity, groupName);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });

        response.Successfull.Should().BeTrue($"Group feed creation should succeed: {response.Message}");

        await blockControl.ProduceBlockAsync();

        _scenarioContext[$"GroupFeed_{groupName}"] = feedId;
        _scenarioContext[$"GroupFeedAesKey_{groupName}"] = aesKey;
    }

    [Given(@"(.*) has created a public group feed ""(.*)""")]
    public async Task GivenUserHasCreatedAPublicGroupFeed(string userName, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var (signedTxJson, feedId, aesKey) = TestTransactionFactory.CreateGroupFeed(identity, groupName, isPublic: true);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });

        response.Successfull.Should().BeTrue($"Public group feed creation should succeed: {response.Message}");

        await blockControl.ProduceBlockAsync();

        _scenarioContext[$"GroupFeed_{groupName}"] = feedId;
        _scenarioContext[$"GroupFeedAesKey_{groupName}"] = aesKey;
    }

    [Given(@"the Redis participants cache for ""(.*)"" is empty")]
    public async Task GivenTheRedisParticipantsCacheForGroupIsEmpty(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        await ClearParticipantsCache(feedId);
    }

    [Given(@"the Redis participants cache for the ChatFeed is empty")]
    public async Task GivenTheRedisParticipantsCacheForTheChatFeedIsEmpty()
    {
        var feedId = GetChatFeedId();
        await ClearParticipantsCache(feedId);
    }

    [Given(@"the Redis key generations cache for ""(.*)"" is empty")]
    public async Task GivenTheRedisKeyGenerationsCacheIsEmpty(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        await redisDb.KeyDeleteAsync(cacheKey);
    }

    [Given(@"(.*) has sent a message to ""(.*)""")]
    public async Task GivenUserHasSentAMessageToGroup(string userName, string groupName)
    {
        await WhenUserSendsMessageToGroupViaGrpc(userName, "Test message", groupName);

        var blockControl = GetBlockControl();
        await blockControl.ProduceBlockAsync();
    }

    [Given(@"the participants are in the Redis SET cache for ""(.*)""")]
    public async Task GivenTheParticipantsAreInTheRedisSETCacheForGroup(string groupName)
    {
        // Verify cache is populated from the message send
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        if (!exists)
        {
            // If not populated yet, send a message to populate it
            await GivenUserHasSentAMessageToGroup("Alice", groupName);
        }
    }

    [Given(@"the group has key generations in the database")]
    public void GivenTheGroupHasKeyGenerationsInTheDatabase()
    {
        // Group was created with initial key generation, so it exists in DB
    }

    [When(@"(.*) sends message ""(.*)"" to group ""(.*)"" via gRPC")]
    public async Task WhenUserSendsMessageToGroupViaGrpc(string userName, string message, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var aesKey = (string)_scenarioContext[$"GroupFeedAesKey_{groupName}"];

        var signedTransactionJson = TestTransactionFactory.CreateFeedMessage(identity, feedId, message, aesKey);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTransactionJson
        });

        response.Successfull.Should().BeTrue($"Message submission should succeed: {response.Message}");

        _scenarioContext[$"LastGroupMessage_{groupName}"] = message;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [When(@"the key generations for ""(.*)"" are looked up via gRPC")]
    public async Task WhenTheKeyGenerationsForGroupAreLookedUpViaGrpc(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var identity = GetTestIdentity("Alice"); // Use Alice's identity for the request
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var response = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = identity.PublicSigningAddress
        });

        _scenarioContext[$"LastKeyGenerationsResponse_{groupName}"] = response;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [When(@"the Redis participants cache for ""(.*)"" is flushed")]
    public async Task WhenTheRedisParticipantsCacheForGroupIsFlushed(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        await ClearParticipantsCache(feedId);
    }

    [When(@"the Redis key generations cache for ""(.*)"" is flushed")]
    public async Task WhenTheRedisKeyGenerationsCacheForGroupIsFlushed(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        await redisDb.KeyDeleteAsync(cacheKey);
    }

    [Then(@"the participants should be in the Redis SET cache for ""(.*)""")]
    public async Task ThenTheParticipantsShouldBeInTheRedisSETCacheForGroup(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        await VerifyParticipantsCacheExists(feedId, groupName);
    }

    [Then(@"the participants should be in the Redis SET cache for the ChatFeed")]
    public async Task ThenTheParticipantsShouldBeInTheRedisSETCacheForTheChatFeed()
    {
        var feedId = GetChatFeedId();
        await VerifyParticipantsCacheExists(feedId, "ChatFeed");
    }

    [Then(@"the cache SET should contain (.*) as a participant")]
    public async Task ThenTheCacheSETShouldContainUserAsAParticipant(string userName)
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var identity = GetTestIdentity(userName);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";

        // Participants cache uses Redis SET
        var members = await redisDb.SetMembersAsync(cacheKey);
        var memberStrings = members.Select(m => m.ToString()).ToList();

        memberStrings.Should().Contain(identity.PublicSigningAddress,
            $"Cache SET should contain {userName} as participant");
    }

    [Then(@"the cache SET should contain both (.*) and (.*)")]
    public async Task ThenTheCacheSETShouldContainBothUsers(string user1, string user2)
    {
        var feedId = GetChatFeedId();
        var identity1 = GetTestIdentity(user1);
        var identity2 = GetTestIdentity(user2);
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";

        // Participants cache uses Redis SET
        var members = await redisDb.SetMembersAsync(cacheKey);
        var memberStrings = members.Select(m => m.ToString()).ToList();

        memberStrings.Should().Contain(identity1.PublicSigningAddress,
            $"Cache SET should contain {user1} as participant");
        memberStrings.Should().Contain(identity2.PublicSigningAddress,
            $"Cache SET should contain {user2} as participant");
    }

    [Then(@"the key generations should be in the Redis cache for ""(.*)""")]
    public async Task ThenTheKeyGenerationsShouldBeInTheRedisCacheForGroup(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        var exists = await redisDb.KeyExistsAsync(cacheKey);

        exists.Should().BeTrue($"Key generations should be in Redis cache for {groupName}");
    }

    [Then(@"the cache should contain at least one key generation")]
    public async Task ThenTheCacheShouldContainAtLeastOneKeyGeneration()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        var cachedValue = await redisDb.StringGetAsync(cacheKey);

        cachedValue.IsNullOrEmpty.Should().BeFalse("Cache should contain key generations JSON");
        cachedValue.ToString().Should().Contain("keyGenerations",
            "Cache should contain valid key generations JSON structure (camelCase)");
    }

    [Then(@"the response should contain the key generations")]
    public void ThenTheResponseShouldContainTheKeyGenerations()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var response = (GetKeyGenerationsResponse)_scenarioContext[$"LastKeyGenerationsResponse_{groupName}"];
        response.KeyGenerations.Should().NotBeEmpty("Response should contain key generations");
    }

    [Given(@"the key generations for ""(.*)"" have been cached")]
    public async Task GivenTheKeyGenerationsHaveBeenCached(string groupName)
    {
        // Trigger cache population via lookup
        await WhenTheKeyGenerationsForGroupAreLookedUpViaGrpc(groupName);
    }

    [When(@"the key generations for ""(.*)"" are looked up via gRPC again")]
    public async Task WhenTheKeyGenerationsForGroupAreLookedUpViaGrpcAgain(string groupName)
    {
        await WhenTheKeyGenerationsForGroupAreLookedUpViaGrpc(groupName);
    }

    [Given(@"(.*) joins the public group ""(.*)"" via gRPC")]
    [When(@"(.*) joins the public group ""(.*)"" via gRPC")]
    public async Task WhenUserJoinsThePublicGroupViaGrpc(string userName, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];

        Console.WriteLine($"[TwinTest] {userName} joining public group '{groupName}' (feedId={feedId.Value})...");

        var response = await feedClient.JoinGroupFeedAsync(new JoinGroupFeedRequest
        {
            FeedId = feedId.ToString(),
            JoiningUserPublicAddress = identity.PublicSigningAddress,
            JoiningUserPublicEncryptKey = identity.PublicEncryptAddress
        });

        Console.WriteLine($"[TwinTest] {userName} join result: Success={response.Success}, Message={response.Message}");

        response.Success.Should().BeTrue($"{userName} should be able to join public group: {response.Message}");

        _scenarioContext[$"JoinResponse_{groupName}_{userName}"] = response;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [Given(@"(.*) looks up key generations for ""(.*)"" via gRPC")]
    [When(@"(.*) looks up key generations for ""(.*)"" via gRPC")]
    public async Task WhenUserLooksUpKeyGenerationsForGroupViaGrpc(string userName, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];

        var response = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = identity.PublicSigningAddress
        });

        // CRITICAL LOGGING: Show exactly what the server returns
        Console.WriteLine($"[TwinTest] {userName} GetKeyGenerations for '{groupName}': received {response.KeyGenerations.Count} KeyGeneration(s)");
        foreach (var kg in response.KeyGenerations)
        {
            Console.WriteLine($"[TwinTest]   - KeyGen {kg.KeyGeneration}: ValidFrom={kg.ValidFromBlock}, HasEncryptedKey={!string.IsNullOrEmpty(kg.EncryptedKey)}, EncryptedKeyLength={kg.EncryptedKey?.Length ?? 0}");
        }

        _scenarioContext[$"KeyGenerationsResponse_{groupName}_{userName}"] = response;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [Then(@"(.*) should receive at least one key generation for ""(.*)""")]
    public void ThenUserShouldReceiveAtLeastOneKeyGenerationForGroup(string userName, string groupName)
    {
        var response = (GetKeyGenerationsResponse)_scenarioContext[$"KeyGenerationsResponse_{groupName}_{userName}"];

        response.KeyGenerations.Should().NotBeEmpty(
            $"{userName} should receive key generations after joining group '{groupName}'. " +
            $"Got {response.KeyGenerations.Count} key generations. " +
            "If this is 0, the key generations cache was not invalidated after join.");
    }

    [Then(@"(.*) should receive exactly (\d+) key generation(?:s)? for ""(.*)""")]
    public void ThenUserShouldReceiveExactlyNKeyGenerationsForGroup(string userName, int expectedCount, string groupName)
    {
        var response = (GetKeyGenerationsResponse)_scenarioContext[$"KeyGenerationsResponse_{groupName}_{userName}"];

        Console.WriteLine($"[TwinTest] ASSERTION: {userName} expects {expectedCount} KeyGeneration(s), got {response.KeyGenerations.Count}");

        response.KeyGenerations.Should().HaveCount(expectedCount,
            $"{userName} should receive exactly {expectedCount} key generation(s) for group '{groupName}'. " +
            $"Got {response.KeyGenerations.Count} key generations. " +
            "This indicates the key rotation did not create a new generation when Bob joined.");

        Console.WriteLine($"[TwinTest] ASSERTION PASSED: {userName} received exactly {expectedCount} KeyGeneration(s)");
    }

    [Then(@"the database should have (\d+) key generations for ""(.*)""")]
    public async Task ThenTheDatabaseShouldHaveNKeyGenerationsForGroup(int expectedCount, string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Query directly via gRPC (which queries the database)
        // First, flush the cache to ensure we get fresh data from DB
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();
        var cacheKey = $"HushTest:feed:{feedId.Value}:keys";
        await redisDb.KeyDeleteAsync(cacheKey);

        // Now query - this will hit the database
        var identity = GetTestIdentity("Alice");
        var response = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = identity.PublicSigningAddress
        });

        response.KeyGenerations.Should().HaveCount(expectedCount,
            $"Database should have {expectedCount} key generation(s) for group '{groupName}'. " +
            $"Got {response.KeyGenerations.Count}. " +
            "This indicates the key rotation did not create a new generation when Bob joined.");
    }

    [Then(@"(.*) should have an encrypted key for KeyGeneration (\d+) in ""(.*)""")]
    public async Task ThenUserShouldHaveEncryptedKeyForKeyGenerationInGroup(string userName, int keyGeneration, string groupName)
    {
        // Query key generations for this specific user
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];

        var response = await feedClient.GetKeyGenerationsAsync(new GetKeyGenerationsRequest
        {
            FeedId = feedId.ToString(),
            UserPublicAddress = identity.PublicSigningAddress
        });

        var keyGen = response.KeyGenerations.FirstOrDefault(kg => kg.KeyGeneration == keyGeneration);
        keyGen.Should().NotBeNull(
            $"KeyGeneration {keyGeneration} should exist in the response for {userName}. " +
            $"Got KeyGenerations: [{string.Join(", ", response.KeyGenerations.Select(k => k.KeyGeneration))}]");

        keyGen!.EncryptedKey.Should().NotBeNullOrEmpty(
            $"{userName} should have an encrypted key for KeyGeneration {keyGeneration} in group '{groupName}'. " +
            "The GetKeyGenerations response should include this user's encrypted AES key. " +
            "This means the key rotation did not generate a key for this user.");
    }

    [When(@"(.*) sends a group message ""(.*)"" to ""(.*)"" with KeyGeneration (\d+)")]
    public async Task WhenUserSendsGroupMessageWithKeyGeneration(string userName, string message, string groupName, int keyGeneration)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var aesKey = (string)_scenarioContext[$"GroupFeedAesKey_{groupName}"];

        var (signedTxJson, messageId) = TestTransactionFactory.CreateGroupFeedMessage(
            identity, feedId, message, aesKey, keyGeneration);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });

        response.Successfull.Should().BeTrue($"Group message submission should succeed: {response.Message}");

        // Store the message ID for verification
        _scenarioContext[$"GroupMessage_{groupName}_{message}"] = messageId;
        _scenarioContext["LastGroupFeedName"] = groupName;

        await blockControl.ProduceBlockAsync();
    }

    [Then(@"the message ""(.*)"" in ""(.*)"" should have KeyGeneration (\d+)")]
    public async Task ThenMessageShouldHaveKeyGeneration(string messageContent, string groupName, int expectedKeyGeneration)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var messageId = (FeedMessageId)_scenarioContext[$"GroupMessage_{groupName}_{messageContent}"];

        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Get messages for Alice (as she's involved in all scenarios)
        var identity = GetTestIdentity("Alice");
        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = identity.PublicSigningAddress,
            BlockIndex = 0
        });

        // Filter messages by feed and message ID
        var message = response.Messages.FirstOrDefault(m =>
            m.FeedId == feedId.ToString() && m.FeedMessageId == messageId.ToString());

        message.Should().NotBeNull(
            $"Message '{messageContent}' should exist in feed '{groupName}'. " +
            $"MessageId: {messageId}. Found {response.Messages.Count} messages total.");

        message!.KeyGeneration.Should().Be(expectedKeyGeneration,
            $"Message '{messageContent}' should have KeyGeneration {expectedKeyGeneration}. " +
            $"Got KeyGeneration {message.KeyGeneration}. This indicates the message was encrypted with the wrong key.");
    }

    [Then(@"KeyGeneration (\d+) for ""(.*)"" should have no ValidToBlock")]
    public void ThenKeyGenerationShouldHaveNoValidToBlock(int keyGeneration, string groupName)
    {
        // Get the last response for Alice (creator) to verify the key state
        var response = (GetKeyGenerationsResponse)_scenarioContext[$"KeyGenerationsResponse_{groupName}_Alice"];

        var keyGen = response.KeyGenerations.FirstOrDefault(kg => kg.KeyGeneration == keyGeneration);
        keyGen.Should().NotBeNull(
            $"KeyGeneration {keyGeneration} should exist in the response. " +
            $"Got KeyGenerations: [{string.Join(", ", response.KeyGenerations.Select(k => k.KeyGeneration))}]");

        // ValidToBlock is optional - if not set, HasValidToBlock should be false
        keyGen!.HasValidToBlock.Should().BeFalse(
            $"KeyGeneration {keyGeneration} should NOT have ValidToBlock set (meaning it's the active key). " +
            $"But it has ValidToBlock={keyGen.ValidToBlock}. This indicates the key was expired when it shouldn't be.");
    }

    [Then(@"KeyGeneration (\d+) for ""(.*)"" should have ValidToBlock set")]
    public void ThenKeyGenerationShouldHaveValidToBlockSet(int keyGeneration, string groupName)
    {
        // Get the last response for Alice (creator) to verify the key state
        var response = (GetKeyGenerationsResponse)_scenarioContext[$"KeyGenerationsResponse_{groupName}_Alice"];

        var keyGen = response.KeyGenerations.FirstOrDefault(kg => kg.KeyGeneration == keyGeneration);
        keyGen.Should().NotBeNull(
            $"KeyGeneration {keyGeneration} should exist in the response. " +
            $"Got KeyGenerations: [{string.Join(", ", response.KeyGenerations.Select(k => k.KeyGeneration))}]");

        // ValidToBlock should be set when a new key generation is created
        keyGen!.HasValidToBlock.Should().BeTrue(
            $"KeyGeneration {keyGeneration} should have ValidToBlock set (meaning it's expired). " +
            "When a new member joins, the previous key generation should be marked with ValidToBlock.");

        keyGen.ValidToBlock.Should().BeGreaterThan(0,
            $"KeyGeneration {keyGeneration} ValidToBlock should be a positive block number.");
    }

    #region Decryption Twin Test Steps

    [When(@"(.*) decrypts (?:her|his) AES key for KeyGeneration (\d+) in ""(.*)""")]
    public void WhenUserDecryptsAesKeyForKeyGeneration(string userName, int keyGeneration, string groupName)
    {
        var identity = GetTestIdentity(userName);
        var response = (GetKeyGenerationsResponse)_scenarioContext[$"KeyGenerationsResponse_{groupName}_{userName}"];

        var keyGen = response.KeyGenerations.FirstOrDefault(kg => kg.KeyGeneration == keyGeneration);
        keyGen.Should().NotBeNull(
            $"{userName} should have KeyGeneration {keyGeneration} in response for {groupName}. " +
            $"Available: [{string.Join(", ", response.KeyGenerations.Select(k => k.KeyGeneration))}]");

        try
        {
            // ECIES decrypt the AES key using user's private encrypt key
            var decryptedAesKey = Olimpo.EncryptKeys.Decrypt(keyGen!.EncryptedKey, identity.PrivateEncryptKey);

            // Store the decrypted key for later use
            _scenarioContext[$"DecryptedAesKey_{groupName}_{userName}_KeyGen{keyGeneration}"] = decryptedAesKey;
            _scenarioContext[$"DecryptionSuccess_{userName}"] = true;

            Console.WriteLine($"[TwinTest] {userName} successfully decrypted KeyGen {keyGeneration} AES key for {groupName}");
        }
        catch (Exception ex)
        {
            _scenarioContext[$"DecryptionSuccess_{userName}"] = false;
            _scenarioContext[$"DecryptionError_{userName}"] = ex.Message;

            Console.WriteLine($"[TwinTest] {userName} FAILED to decrypt KeyGen {keyGeneration}: {ex.Message}");
        }
    }

    [Then(@"the decryption should succeed for (.*)")]
    public void ThenTheDecryptionShouldSucceedForUser(string userName)
    {
        var success = (bool)_scenarioContext[$"DecryptionSuccess_{userName}"];

        if (!success)
        {
            var error = _scenarioContext.TryGetValue($"DecryptionError_{userName}", out var errObj)
                ? (string)errObj
                : "Unknown error";

            success.Should().BeTrue(
                $"{userName}'s AES key decryption should succeed. Error: {error}. " +
                "This means the server returned an encrypted key that the user cannot decrypt with their private key.");
        }
    }

    [When(@"(.*) sends a group message ""(.*)"" to ""(.*)"" using (?:her|his) decrypted KeyGeneration (\d+) key")]
    public async Task WhenUserSendsGroupMessageUsingDecryptedKey(string userName, string message, string groupName, int keyGeneration)
    {
        var identity = GetTestIdentity(userName);
        var grpcFactory = GetGrpcFactory();
        var blockControl = GetBlockControl();
        var blockchainClient = grpcFactory.CreateClient<HushBlockchain.HushBlockchainClient>();

        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];

        // Get the user's decrypted AES key for this KeyGeneration
        var decryptedAesKey = (string)_scenarioContext[$"DecryptedAesKey_{groupName}_{userName}_KeyGen{keyGeneration}"];

        var (signedTxJson, messageId) = TestTransactionFactory.CreateGroupFeedMessage(
            identity, feedId, message, decryptedAesKey, keyGeneration);

        var response = await blockchainClient.SubmitSignedTransactionAsync(new SubmitSignedTransactionRequest
        {
            SignedTransaction = signedTxJson
        });

        response.Successfull.Should().BeTrue($"Group message submission should succeed: {response.Message}");

        // Store message info for verification
        _scenarioContext[$"GroupMessage_{groupName}_{message}"] = messageId;
        _scenarioContext[$"GroupMessageEncrypted_{groupName}_{message}"] = Olimpo.EncryptKeys.AesEncrypt(message, decryptedAesKey);
        _scenarioContext["LastGroupFeedName"] = groupName;

        Console.WriteLine($"[TwinTest] {userName} sent message '{message}' encrypted with KeyGen {keyGeneration}");

        await blockControl.ProduceBlockAsync();
    }

    [Then(@"(.*) should be able to decrypt (.*)'s message ""(.*)"" in ""(.*)""")]
    public async Task ThenUserShouldBeAbleToDecryptMessage(string decryptingUser, string sendingUser, string expectedMessage, string groupName)
    {
        var decryptingIdentity = GetTestIdentity(decryptingUser);
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var messageId = (FeedMessageId)_scenarioContext[$"GroupMessage_{groupName}_{expectedMessage}"];

        var grpcFactory = GetGrpcFactory();
        var feedClient = grpcFactory.CreateClient<HushFeed.HushFeedClient>();

        // Get the message from the server
        var response = await feedClient.GetFeedMessagesForAddressAsync(new GetFeedMessagesForAddressRequest
        {
            ProfilePublicKey = decryptingIdentity.PublicSigningAddress,
            BlockIndex = 0
        });

        var message = response.Messages.FirstOrDefault(m =>
            m.FeedId == feedId.ToString() && m.FeedMessageId == messageId.ToString());

        message.Should().NotBeNull(
            $"Message '{expectedMessage}' from {sendingUser} should exist in feed '{groupName}'. " +
            $"MessageId: {messageId}");

        // Get the KeyGeneration used for this message
        var messageKeyGen = message!.KeyGeneration;
        Console.WriteLine($"[TwinTest] Message was encrypted with KeyGen {messageKeyGen}");

        // Get the decrypting user's AES key for this KeyGeneration
        var contextKey = $"DecryptedAesKey_{groupName}_{decryptingUser}_KeyGen{messageKeyGen}";
        _scenarioContext.TryGetValue(contextKey, out var aesKeyObj).Should().BeTrue(
            $"{decryptingUser} should have decrypted AES key for KeyGen {messageKeyGen}. " +
            $"This simulates the browser having this key in localStorage after sync.");

        var decryptedAesKey = (string)aesKeyObj!;

        // Decrypt the message content
        try
        {
            var decryptedContent = Olimpo.EncryptKeys.AesDecrypt(message.MessageContent, decryptedAesKey);

            decryptedContent.Should().Be(expectedMessage,
                $"{decryptingUser} should be able to decrypt {sendingUser}'s message to '{expectedMessage}'");

            Console.WriteLine($"[TwinTest] SUCCESS: {decryptingUser} decrypted message: '{decryptedContent}'");
        }
        catch (Exception ex)
        {
            throw new Exception(
                $"{decryptingUser} FAILED to decrypt {sendingUser}'s message '{expectedMessage}' " +
                $"using KeyGen {messageKeyGen}. Error: {ex.Message}. " +
                "This is the EXACT issue that causes the Playwright E2E test to fail - " +
                "Alice receives a message encrypted with a key she cannot decrypt.", ex);
        }
    }

    #endregion

    [When(@"the participants cache service stores participants for ""(.*)""")]
    public async Task WhenTheParticipantsCacheServiceStoresParticipantsForGroup(string groupName)
    {
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var identity = GetTestIdentity("Alice");
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        // Directly store participants in Redis SET (simulating what the cache service does)
        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        var participants = new[] { identity.PublicSigningAddress };

        // Use Redis SET to store participants
        await redisDb.SetAddAsync(cacheKey, participants.Select(p => (RedisValue)p).ToArray());

        _scenarioContext[$"StoredParticipants_{groupName}"] = participants;
        _scenarioContext["LastGroupFeedName"] = groupName;
    }

    [Then(@"the participants should be retrievable from the cache service")]
    public async Task ThenTheParticipantsShouldBeRetrievableFromTheCacheService()
    {
        var groupName = (string)_scenarioContext["LastGroupFeedName"];
        var feedId = (FeedId)_scenarioContext[$"GroupFeed_{groupName}"];
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        var members = await redisDb.SetMembersAsync(cacheKey);

        members.Should().NotBeEmpty("Participants should be retrievable from cache");

        var storedParticipants = (string[])_scenarioContext[$"StoredParticipants_{groupName}"];
        var memberStrings = members.Select(m => m.ToString()).ToList();

        foreach (var participant in storedParticipants)
        {
            memberStrings.Should().Contain(participant, $"Cache should contain stored participant");
        }
    }

    #region Helper Methods

    private async Task ClearParticipantsCache(FeedId feedId)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";
        await redisDb.KeyDeleteAsync(cacheKey);

        var exists = await redisDb.KeyExistsAsync(cacheKey);
        exists.Should().BeFalse("Participants cache should be empty after deletion");
    }

    private async Task VerifyParticipantsCacheExists(FeedId feedId, string feedName)
    {
        var fixture = GetFixture();
        var redisDb = fixture.RedisConnection.GetDatabase();

        var cacheKey = $"HushTest:feed:{feedId.Value}:participants";

        // Retry with small delays to handle async fire-and-forget cache population
        var maxAttempts = 10;
        for (var attempt = 0; attempt < maxAttempts; attempt++)
        {
            var exists = await redisDb.KeyExistsAsync(cacheKey);
            if (exists)
            {
                return; // Cache exists, test passes
            }
            await Task.Delay(100); // Wait 100ms between attempts
        }

        // Final check with assertion
        var finalExists = await redisDb.KeyExistsAsync(cacheKey);
        finalExists.Should().BeTrue($"Participants should be in Redis SET cache for {feedName}");
    }

    private FeedId GetChatFeedId()
    {
        foreach (var key in _scenarioContext.Keys)
        {
            if (key.StartsWith("ChatFeed_") && !key.Contains("AesKey"))
            {
                return (FeedId)_scenarioContext[key];
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

    private BlockProductionControl GetBlockControl()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.BlockControlKey, out var controlObj)
            && controlObj is BlockProductionControl blockControl)
        {
            return blockControl;
        }
        throw new InvalidOperationException("BlockProductionControl not found in ScenarioContext.");
    }

    private HushTestFixture GetFixture()
    {
        if (_scenarioContext.TryGetValue(ScenarioHooks.FixtureKey, out var fixtureObj)
            && fixtureObj is HushTestFixture fixture)
        {
            return fixture;
        }
        throw new InvalidOperationException("HushTestFixture not found in ScenarioContext.");
    }

    #endregion
}
