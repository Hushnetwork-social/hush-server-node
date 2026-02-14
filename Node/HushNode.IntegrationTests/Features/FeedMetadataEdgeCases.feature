@Integration
Feature: FEAT-065 Feed Metadata Edge Cases
  As a cache system
  I want to handle edge cases gracefully
  So that the system remains robust under non-ideal conditions

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  # EC: Cache miss fallback — GetFeedsForAddress works with empty cache
  @FEAT-065 @EdgeCase
  Scenario: Cache miss triggers PostgreSQL fallback and repopulates cache
    Given Alice has a ChatFeed with Bob
    And Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    When Alice's feed_meta Hash is flushed
    And GetFeedsForAddress is called for Alice
    Then Alice's feed_meta Hash should be repopulated with the ChatFeed
    And Alice's feed_meta Hash should have more than 0 entries

  # EC: Identity name change with flushed feed_meta — cascade gracefully skips
  @FEAT-065 @EdgeCase
  Scenario: Identity name change with flushed cache does not error
    Given Alice has a ChatFeed with Bob
    When Alice's feed_meta Hash is flushed
    And Bob changes display name to "Robert" via gRPC
    And a block is produced
    Then the identity display names Hash should contain "Robert" for Bob's address
    And GetFeedsForAddress is called for Alice
    And Alice's feed_meta Hash should be repopulated with the ChatFeed

  # EC: New message for uncached feed — UpdateLastBlockIndex skips gracefully
  @FEAT-065 @EdgeCase
  Scenario: New message for uncached feed does not error
    Given Alice has a ChatFeed with Bob
    When Alice's feed_meta Hash is flushed
    And Alice sends message "Hello uncached!" to the ChatFeed via gRPC
    And a block is produced
    Then GetFeedsForAddress is called for Alice
    And Alice's feed_meta Hash should be repopulated with the ChatFeed

  # EC: Warm cache serves correct data on second sync
  @FEAT-065 @F6-009
  Scenario: Second sync with warm cache returns correct feed list
    Given Alice has a ChatFeed with Bob
    And Alice sends message "First message" to the ChatFeed via gRPC
    And a block is produced
    When GetFeedsForAddress is called for Alice
    Then Alice's feed_meta Hash should be repopulated with the ChatFeed
    When GetFeedsForAddress is called for Alice
    Then Alice's feed_meta Hash should be repopulated with the ChatFeed

  # EC: Display name batch with single issuer
  @FEAT-065 @EdgeCase
  Scenario: Display name batch works with single issuer
    Given Alice has a ChatFeed with Bob
    And Alice sends message "Solo message" to the ChatFeed via gRPC
    And a block is produced
    When Alice performs a full sync via GetFeedMessagesForAddress
    Then the response should contain messages with resolved display names
