@Integration
Feature: FEAT-065 Identity Display Name Cache (E2 — global Redis Hash)
  As a message resolver
  I want batch HMGET display name lookups from a global Redis Hash
  So that N message senders don't require N PostgreSQL queries

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available

  # F6-006: Batch HMGET returns all display names
  @FEAT-065 @F6-006
  Scenario: Batch HMGET returns all cached display names
    Given display names are cached for "0xalice" as "Alice" and "0xbob" as "Bob"
    When GetDisplayNamesAsync is called for addresses "0xalice" and "0xbob"
    Then the result should contain "Alice" for "0xalice" and "Bob" for "0xbob"
    And the identity CacheHits counter should be 2
    And the identity CacheMisses counter should be 0

  # F6-006-partial: Partial cache hit returns null for missing
  @FEAT-065 @F6-006-partial
  Scenario: Partial cache hit returns null for uncached addresses
    Given a single display name is cached for "0xalice" as "Alice"
    When GetDisplayNamesAsync is called for addresses "0xalice" and "0xcarol"
    Then the result should contain "Alice" for "0xalice" and null for "0xcarol"
    And the identity CacheHits counter should be 1
    And the identity CacheMisses counter should be 1

  # F6-007: Complete cache miss
  @FEAT-065 @F6-007
  Scenario: Complete cache miss returns all null values
    When GetDisplayNamesAsync is called for addresses "0xunknown1" and "0xunknown2"
    Then the result should contain null for "0xunknown1" and null for "0xunknown2"
    And the identity CacheMisses counter should be 2

  # F6-007 supplementary: Populate after miss, then verify hit
  @FEAT-065 @F6-007
  Scenario: Populate after miss enables subsequent cache hit
    When GetDisplayNamesAsync is called for addresses "0xalice" and "0xbob"
    Then the result should contain null for "0xalice" and null for "0xbob"
    When display names are written for "0xalice" as "Alice" and "0xbob" as "Bob"
    And GetDisplayNamesAsync is called again for addresses "0xalice" and "0xbob"
    Then the second result should contain "Alice" for "0xalice" and "Bob" for "0xbob"

  # Verify Redis content directly
  @FEAT-065 @F6-006
  Scenario: Display names stored as plain strings in Redis Hash
    Given display names are cached for "0xalice" as "Alice" and "0xbob" as "Bob"
    Then the Redis identities:display_names Hash should contain "Alice" for field "0xalice"
    And the Redis identities:display_names Hash should contain "Bob" for field "0xbob"

  # No TTL for identity display names
  @FEAT-065 @F6-006
  Scenario: Identity display names Hash has no TTL
    Given a single display name is cached for "0xalice" as "Alice"
    Then the Redis identities:display_names Hash should have no TTL
