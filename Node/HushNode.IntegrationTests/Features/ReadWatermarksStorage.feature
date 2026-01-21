@Integration
Feature: FEAT-051 Read Watermarks Storage
  As a user with multiple devices
  I want the server to track where I last read
  So that read status syncs across devices

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  # Write-Through Pattern (Database + Cache)
  @FEAT-051 @WriteThrough
  Scenario: Mark feed as read updates both database and cache
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello!" to the ChatFeed via gRPC
    And a block is produced
    And Alice marks the ChatFeed as read at block 2 via gRPC
    Then the read position should be stored in the PostgreSQL FeedReadPosition table
    And the read position should be in the Redis read watermark cache
    And the response should indicate success

  @FEAT-051 @TTL
  Scenario: Read watermark cache has 30-day TTL
    Given Alice has a ChatFeed with Bob
    When Alice marks the ChatFeed as read at block 1 via gRPC
    Then the Redis read watermark cache TTL should be approximately 30 days

  # Max Wins Semantics
  @FEAT-051 @MaxWins
  Scenario: Read watermark only increases (max wins)
    Given Alice has a ChatFeed with Bob
    And Alice has marked the ChatFeed as read at block 100
    When Alice marks the ChatFeed as read at block 50 via gRPC
    Then the read position should remain at block 100

  @FEAT-051 @MaxWins
  Scenario: Read watermark updates when new position is higher
    Given Alice has a ChatFeed with Bob
    And Alice has marked the ChatFeed as read at block 100
    When Alice marks the ChatFeed as read at block 150 via gRPC
    Then the read position should be updated to block 150

  # GetFeedsForAddress Includes Watermarks
  @FEAT-051 @GetFeeds
  Scenario: GetFeedsForAddress includes last read block index from cache
    Given Alice has a ChatFeed with Bob
    And Alice has marked the ChatFeed as read at block 100
    When Alice requests her feeds via GetFeedsForAddress gRPC
    Then the response should include lastReadBlockIndex of 100 for the ChatFeed

  @FEAT-051 @GetFeeds
  Scenario: GetFeedsForAddress falls back to database on cache miss
    Given Alice has a ChatFeed with Bob
    And Alice has marked the ChatFeed as read at block 100
    And the Redis read watermark cache is flushed
    When Alice requests her feeds via GetFeedsForAddress gRPC
    Then the response should include lastReadBlockIndex from the database
    And the read watermark cache should be repopulated

  # Database Persistence for Durability
  @FEAT-051 @Durability
  Scenario: Read watermarks survive Redis restart
    Given Alice has a ChatFeed with Bob
    And Alice has marked the ChatFeed as read at block 100
    And the read position is stored in both database and cache
    When the Redis cache is flushed
    And Alice requests her feeds via GetFeedsForAddress gRPC
    Then the watermark should be retrieved from the database
    And the read watermark cache should be repopulated
    And the response should include lastReadBlockIndex of 100 for the ChatFeed

  # Database Schema Verification
  @FEAT-051 @DatabaseSchema
  Scenario: FeedReadPosition table exists with required schema
    Then the PostgreSQL table "Feeds"."FeedReadPosition" should exist
    And the table should have column "UserId"
    And the table should have column "FeedId"
    And the table should have column "LastReadBlockIndex"
    And the table should have column "UpdatedAt"
    And there should be a unique index on UserId and FeedId in "Feeds"."FeedReadPosition"
