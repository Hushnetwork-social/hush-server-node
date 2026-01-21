@Integration
Feature: FEAT-048 Identity Lookups Cache
  As a client displaying messages
  I want user identities cached
  So that looking up sender display names doesn't query the database

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available

  # Cache-Aside Pattern
  @FEAT-048 @CacheAside
  Scenario: Identity is cached on first lookup
    Given a user profile exists in the database with address "test-addr-001" and alias "Alice Smith"
    And the Redis identity cache has no entry for "test-addr-001"
    When the identity for "test-addr-001" is looked up via gRPC
    Then the response should contain display name "Alice Smith"
    And the identity should be in the Redis cache for "test-addr-001"

  @FEAT-048 @CacheAside
  Scenario: Identity is returned from cache on subsequent lookups
    Given a user profile exists in the database with address "test-addr-002" and alias "Bob Jones"
    And the identity for "test-addr-002" has been looked up once to populate the cache
    When the identity for "test-addr-002" is looked up via gRPC
    Then the response should contain display name "Bob Jones"
    And the identity should still be in the Redis cache for "test-addr-002"

  @FEAT-048 @CacheAside
  Scenario: Non-existing identity is not cached
    Given no user profile exists for address "nonexistent-addr-999"
    When the identity for "nonexistent-addr-999" is looked up via gRPC
    Then the lookup should return not found
    And no cache entry should exist for "nonexistent-addr-999"

  @FEAT-048 @TTL
  Scenario: Identity cache has 7-day TTL
    Given a user profile exists in the database with address "test-addr-003" and alias "Charlie TTL"
    When the identity for "test-addr-003" is looked up via gRPC
    Then the Redis identity cache TTL should be between 6 and 7 days

  # Cache Invalidation
  @FEAT-048 @Invalidation
  Scenario: Cache is repopulated after invalidation
    Given a user profile exists in the database with address "test-addr-004" and alias "Dave Invalidated"
    And the identity for "test-addr-004" is in the Redis cache
    When the Redis identity cache key for "test-addr-004" is deleted
    And the identity for "test-addr-004" is looked up via gRPC
    Then the response should contain display name "Dave Invalidated"
    And the identity should be in the Redis cache for "test-addr-004"

  # Database Verification
  @FEAT-048 @DatabaseSchema
  Scenario: Identity Profile table exists in database
    Then the PostgreSQL table "Identity"."Profile" should exist
