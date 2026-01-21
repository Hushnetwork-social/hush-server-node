@Integration
Feature: FEAT-049 User Feeds List Cache
  As the sync endpoint
  I want user feed lists cached
  So that idle sync cycles don't hit the database

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed

  # Cache-Aside Pattern
  @FEAT-049 @CacheAside
  Scenario: User feed list is cached on first query
    Given Alice's user feeds cache is empty
    When Alice requests feed messages via GetFeedMessagesForAddress gRPC
    Then Alice's feed list should be in the Redis user feeds cache
    And the cache should contain Alice's personal feed ID

  @FEAT-049 @CacheAside
  Scenario: User feed list is returned from cache on subsequent queries
    Given Alice's user feeds cache has been populated
    When Alice requests feed messages via GetFeedMessagesForAddress gRPC again
    Then Alice's feed list should still be in the Redis user feeds cache

  @FEAT-049 @TTL
  Scenario: User feed list cache has 5-minute TTL
    When Alice requests feed messages via GetFeedMessagesForAddress gRPC
    Then the Redis user feeds cache TTL should be less than or equal to 5 minutes

  # In-Place Updates (SADD)
  @FEAT-049 @InPlaceUpdate
  Scenario: Creating group feed adds to user's cached feed list
    Given Alice's user feeds cache has been populated
    And Alice's user feeds cache count is recorded
    When Alice creates a group feed "TestGroup" via gRPC
    And a block is produced
    Then Alice's Redis user feeds cache should contain one more feed ID

  @FEAT-049 @InPlaceUpdate
  Scenario: Creating chat feed updates both participants' cached feed lists
    Given user "Bob" is registered with a personal feed
    And Alice's user feeds cache has been populated
    And Bob's user feeds cache has been populated
    And Alice's user feeds cache count is recorded
    And Bob's user feeds cache count is recorded
    When Alice creates a ChatFeed with Bob via gRPC
    And a block is produced
    Then Alice's Redis user feeds cache should contain one more feed ID
    And Bob's Redis user feeds cache should contain one more feed ID

  # Fallback Behavior
  @FEAT-049 @Fallback
  Scenario: Feed list query works after cache flush
    Given Alice has feeds in the database
    And the Redis cache is flushed
    When Alice requests feed messages via GetFeedMessagesForAddress gRPC
    Then the response should succeed
    And Alice's feed list should be in the Redis user feeds cache
