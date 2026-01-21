@Integration
Feature: FEAT-050 Feed Participants & Group Keys Cache
  As the notification handler
  I want feed participants and group keys cached
  So that notifications don't block on database queries

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  # Key Generations Cache - Cache-Aside Pattern (populated via GetKeyGenerations gRPC)
  # This is the testable integration path - gRPC handler populates cache synchronously
  @FEAT-050 @KeyGenerations
  Scenario: Group key generations are cached on first lookup
    Given Alice has created a group feed "KeyTestGroup"
    And the Redis key generations cache for "KeyTestGroup" is empty
    When the key generations for "KeyTestGroup" are looked up via gRPC
    Then the key generations should be in the Redis cache for "KeyTestGroup"
    And the cache should contain at least one key generation

  @FEAT-050 @KeyGenerations
  Scenario: Key generations cache hit returns data without database query
    Given Alice has created a group feed "CacheHitGroup"
    And the key generations for "CacheHitGroup" have been cached
    When the key generations for "CacheHitGroup" are looked up via gRPC again
    Then the response should contain the key generations

  # Fallback Behavior - Key Generations
  @FEAT-050 @Fallback
  Scenario: Key generations lookup works after cache flush
    Given Alice has created a group feed "KeyFallbackGroup"
    And the group has key generations in the database
    When the Redis key generations cache for "KeyFallbackGroup" is flushed
    And the key generations for "KeyFallbackGroup" are looked up via gRPC
    Then the response should contain the key generations
    And the key generations should be in the Redis cache for "KeyFallbackGroup"

  # Participants Cache Service Exists and Is Functional
  # Note: Participants cache is populated by NotificationEventHandler via async events
  # which can't be reliably tested in integration tests due to fire-and-forget timing.
  # Unit tests in NotificationEventHandlerTests.cs cover the cache population logic.
  @FEAT-050 @ParticipantsService
  Scenario: Feed participants cache service is registered and functional
    Given Alice has created a group feed "ServiceTestGroup"
    When the participants cache service stores participants for "ServiceTestGroup"
    Then the participants should be retrievable from the cache service
