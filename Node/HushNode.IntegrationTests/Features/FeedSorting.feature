@Integration
Feature: FEAT-062 Feed Sorting by blockIndex
  As a sync endpoint
  I want GetFeedsForAddress to return feeds with correct blockIndex values
  So that clients can sort feeds by most recent activity

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And user "Charlie" is registered with a personal feed

  # Verify feeds are returned with correct relative blockIndex ordering after messages
  @FEAT-062 @F2-INT-001
  Scenario: GetFeedsForAddress returns feeds with correct blockIndex after messages
    Given Alice has a ChatFeed with Bob
    And Alice has a ChatFeed with Charlie
    When Alice sends message "Hi Bob" to ChatFeed(Alice,Bob) via gRPC
    And a block is produced
    When Alice sends message "Hi Charlie" to ChatFeed(Alice,Charlie) via gRPC
    And a block is produced
    When Alice requests her feeds via GetFeedsForAddress gRPC
    Then ChatFeed(Alice,Charlie) should have a higher BlockIndex than ChatFeed(Alice,Bob)
    And the Redis feed_meta Hash for Alice should contain lastBlockIndex for ChatFeed(Alice,Bob)
    And the Redis feed_meta Hash for Alice should contain lastBlockIndex for ChatFeed(Alice,Charlie)

  # Verify blockIndex increases monotonically as messages are sent
  @FEAT-062 @F2-INT-002
  Scenario: Multiple messages increase blockIndex monotonically
    Given Alice has a ChatFeed with Bob
    When Alice sends message "First" to the ChatFeed via gRPC
    And a block is produced
    And the ChatFeed BlockIndex is recorded as "firstBlockIndex"
    When Alice sends message "Second" to the ChatFeed via gRPC
    And a block is produced
    When Alice requests her feeds via GetFeedsForAddress gRPC
    Then the ChatFeed BlockIndex should be greater than "firstBlockIndex"
    And the Redis feed_meta Hash lastBlockIndex should be consistent with the response BlockIndex

  # Verify Redis cache contains the correct blockIndex after message finalization
  # NOTE: The gRPC response uses effectiveBlockIndex = MAX(feedBlockIndex, participantProfileBlockIndex),
  # so it may be higher than the raw Redis lastBlockIndex when a participant was registered at a later block.
  @FEAT-062 @F2-INT-003
  Scenario: Redis cache provides feed blockIndex without PostgreSQL round trip
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    When Alice requests her feeds via GetFeedsForAddress gRPC
    Then the Redis feed_meta Hash for Alice should contain lastBlockIndex for the ChatFeed
    And the response BlockIndex should be greater than or equal to the Redis cached value

  # Verify both participants of a shared feed see the same blockIndex
  @FEAT-062 @F2-INT-004
  Scenario: Both participants see same blockIndex for shared feed
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    When Alice requests her feeds and stores as "AliceFeeds"
    And Bob requests his feeds and stores as "BobFeeds"
    Then both "AliceFeeds" and "BobFeeds" should have the same BlockIndex for the shared ChatFeed
