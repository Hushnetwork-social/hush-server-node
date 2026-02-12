@Integration
Feature: FEAT-060 Redis-First Caching (Read Positions Hash + Feed lastBlockIndex)
  As a sync endpoint
  I want read positions stored in Redis Hash and feed lastBlockIndex cached
  So that idle sync cycles avoid per-key round trips and extra PostgreSQL queries

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  # Read Positions Hash — Cache Miss → PostgreSQL Fallback → Populate Hash
  @FEAT-060 @F5-004
  Scenario: Read positions cache miss falls back to PostgreSQL and populates Hash
    Given Alice has a ChatFeed with Bob
    And Alice has marked the ChatFeed as read at block 100
    And the Redis read watermark cache is flushed
    When Alice requests her feeds via GetFeedsForAddress gRPC
    Then the response should include lastReadBlockIndex of 100 for the ChatFeed
    And the read watermark cache should be repopulated

  # Feed lastBlockIndex — Updated on message finalization
  @FEAT-060 @F5-005
  Scenario: Feed lastBlockIndex updated in Redis on message finalization
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    Then the Redis feed_meta Hash for Alice should contain lastBlockIndex for the ChatFeed
    And the Redis feed_meta Hash for Bob should contain lastBlockIndex for the ChatFeed

  # Feed lastBlockIndex — Served from Redis via GetFeedsForAddress
  @FEAT-060 @F5-006
  Scenario: GetFeedsForAddress returns lastBlockIndex from Redis
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    And Alice requests her feeds via GetFeedsForAddress gRPC
    Then the ChatFeed response should have a non-zero lastBlockIndex

  # Redis failure graceful degradation
  @FEAT-060 @F5-007
  Scenario: Redis failure degrades gracefully to PostgreSQL
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    And the Redis cache is flushed
    And Alice requests her feeds via GetFeedsForAddress gRPC
    Then the response should contain the ChatFeed
    And no error is returned to the client

  # Cross-feature: Redis lastBlockIndex feeds client sorting
  @FEAT-060 @CF-004
  Scenario: Redis lastBlockIndex reflects latest message block
    Given Alice has a ChatFeed with Bob
    When Alice sends message "First" to the ChatFeed via gRPC
    And a block is produced
    And Alice sends message "Second" to the ChatFeed via gRPC
    And a block is produced
    And Alice requests her feeds via GetFeedsForAddress gRPC
    Then the ChatFeed response should have lastBlockIndex greater than 1

  # Cross-feature: Redis read positions Hash feeds read sync
  @FEAT-060 @CF-005
  Scenario: MarkFeedAsRead updates both PostgreSQL and Redis Hash
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    And Alice marks the ChatFeed as read at block 2 via gRPC
    Then the read position should be stored in the PostgreSQL FeedReadPosition table
    And the read position should be in the Redis read watermark cache

  # Edge case: Redis restart with cold cache triggers repopulation
  @FEAT-060 @EC-005
  Scenario: Redis restart with cold cache triggers full repopulation
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    And Alice marks the ChatFeed as read at block 2 via gRPC
    And the Redis cache is flushed
    And Alice requests her feeds via GetFeedsForAddress gRPC
    Then the response should contain the ChatFeed
    And the read watermark cache should be repopulated
