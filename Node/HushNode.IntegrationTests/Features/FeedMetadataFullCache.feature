@Integration
Feature: FEAT-065 Feed Metadata Full Cache (E1 — 6-field Redis Hash)
  As a feed list endpoint
  I want full feed metadata stored in a per-user Redis Hash
  So that GetFeedsForAddress can serve the complete feed list from a single HGETALL

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available

  # F6-001: HGETALL returns complete feed list with full metadata
  @FEAT-065 @F6-001
  Scenario: HGETALL returns all feeds with full 6-field metadata
    Given feed metadata for user "Alice" is populated in Redis with 3 feeds
    When GetAllFeedMetadataAsync is called for user "Alice"
    Then a dictionary of 3 entries is returned
    And each FeedMetadataEntry has title, type, lastBlockIndex, participants, createdAtBlock fields
    And the CacheHits counter should be 1

  # F6-001 supplementary: verify JSON stored in Redis has correct structure
  @FEAT-065 @F6-001
  Scenario: Redis Hash fields contain correctly structured JSON
    Given feed metadata for user "Alice" is populated in Redis with 3 feeds
    Then the Redis feed_meta Hash for "Alice" should have 3 fields
    And each field JSON should contain keys title, type, lastBlockIndex, participants, createdAtBlock

  # F6-005: Cache miss falls back gracefully
  @FEAT-065 @F6-005
  Scenario: Cache miss returns null when Hash does not exist
    When GetAllFeedMetadataAsync is called for user "NonExistent"
    Then the result should be null
    And the CacheMisses counter should be 1

  # F6-005 supplementary: repopulate after miss
  @FEAT-065 @F6-005
  Scenario: Cache miss followed by populate yields cache hit
    When GetAllFeedMetadataAsync is called for user "Alice"
    Then the result should be null
    When feed metadata for user "Alice" is populated in Redis with 2 feeds
    And GetAllFeedMetadataAsync is called for user "Alice"
    Then a dictionary of 2 entries is returned

  # Lazy migration: legacy FEAT-060 entries trigger cache miss
  @FEAT-065 @F6-005
  Scenario: Legacy FEAT-060 format triggers lazy migration cache miss
    Given legacy FEAT-060 feed metadata is written for user "Alice"
    When GetAllFeedMetadataAsync is called for user "Alice"
    Then the result should be null
    And the CacheMisses counter should be at least 1

  # TTL verification
  @FEAT-065 @F6-001
  Scenario: Feed metadata Hash has approximately 24-hour TTL after write
    Given feed metadata for user "Alice" is populated in Redis with 1 feed
    Then the Redis feed_meta Hash for "Alice" should have a TTL between 23 and 25 hours
