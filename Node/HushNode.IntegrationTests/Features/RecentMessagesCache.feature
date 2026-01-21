@Integration
Feature: FEAT-046 Recent Messages Cache
  As a client performing sync
  I want to read messages from the cache
  So that sync operations don't query the database directly

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  # Write-Through Pattern
  @FEAT-046 @WriteThrough
  Scenario: New message is cached after blockchain commit
    Given Alice has a ChatFeed with Bob
    And the Redis message cache for the ChatFeed is empty
    When Alice sends message "Hello Bob!" to the ChatFeed via gRPC
    And a block is produced
    Then the message should be stored in the PostgreSQL database
    And the message should be in the Redis message cache for the ChatFeed

  @FEAT-046 @WriteThrough
  Scenario: Cache trims to 100 messages on write
    Given Alice has a ChatFeed with Bob
    And the ChatFeed has 100 messages in cache
    When Alice sends message "Message 101" to the ChatFeed via gRPC
    And a block is produced
    Then the Redis message cache should contain exactly 100 messages

  # Read Path - Cache Miss (Cache-Aside)
  @FEAT-046 @CacheAside
  Scenario: Cache miss populates cache from database
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Test message" to the ChatFeed via gRPC
    And a block is produced
    And the Redis cache is flushed
    And Alice requests messages for the ChatFeed via gRPC
    Then the response should contain the message
    And the Redis message cache should be populated

  # Fallback - Redis Unavailable (simulated by flush)
  @FEAT-046 @Fallback
  Scenario: Client sync works after Redis flush
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Fallback test" to the ChatFeed via gRPC
    And a block is produced
    And the Redis cache is flushed
    And Alice requests messages for the ChatFeed via gRPC
    Then the response should contain the message

  # Database Index Verification
  @FEAT-046 @DatabaseSchema
  Scenario: Database has required indexes for cache fallback
    Then the PostgreSQL index "IX_FeedMessage_FeedId_BlockIndex" should exist on "Feeds"."FeedMessage"
    And the PostgreSQL index "IX_FeedMessage_IssuerPublicAddress_BlockIndex" should exist on "Feeds"."FeedMessage"
