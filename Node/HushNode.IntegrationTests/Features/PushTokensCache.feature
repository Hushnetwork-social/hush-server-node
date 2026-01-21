@Integration
Feature: FEAT-047 Push Tokens Cache
  As the push delivery service
  I want device tokens cached
  So that push notifications are delivered faster

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available

  # Write-Through Pattern
  @FEAT-047 @WriteThrough
  Scenario: Push token is cached on registration
    When user "alice-test-user" registers device token "fcm-token-abc-123" on platform "Android" via gRPC
    Then the token should be stored in the PostgreSQL DeviceTokens table
    And the token should be in the Redis push token cache for "alice-test-user"

  @FEAT-047 @WriteThrough
  Scenario: Push token cache is updated on token refresh
    Given user "alice-test-user" has registered device token "old-token" on platform "Android"
    When user "alice-test-user" registers device token "new-token" on platform "Android" with device name "Updated Device" via gRPC
    Then the Redis push token cache should contain "new-token"

  @FEAT-047 @WriteThrough
  Scenario: Push token is removed from cache on unregistration
    Given user "alice-test-user" has registered device token "token-to-remove" on platform "Android"
    And the token is in the Redis push token cache
    When user "alice-test-user" unregisters token "token-to-remove" via gRPC
    Then the token should be removed from the Redis push token cache

  @FEAT-047 @WriteThrough
  Scenario: Push token reassignment updates both user caches
    Given user "bob-test-user" has registered device token "shared-token" on platform "Android"
    And the token is in the Redis push token cache for "bob-test-user"
    When user "charlie-test-user" registers device token "shared-token" on platform "Android" via gRPC
    Then the token should be in the Redis push token cache for "charlie-test-user"
    And the token should be removed from the Redis push token cache for "bob-test-user"

  @FEAT-047 @TTL
  Scenario: Push token cache has 7-day TTL
    When user "alice-test-user" registers device token "ttl-test-token" on platform "Android" via gRPC
    Then the Redis push token cache TTL should be between 6 and 7 days

  # Fallback Behavior
  @FEAT-047 @Fallback
  Scenario: Token registration works after cache flush
    Given the Redis cache is flushed
    When user "alice-test-user" registers device token "post-flush-token" on platform "Android" via gRPC
    Then the token should be stored in the PostgreSQL DeviceTokens table
    And the token should be in the Redis push token cache for "alice-test-user"

  # GetActiveDeviceTokens
  @FEAT-047 @Query
  Scenario: GetActiveDeviceTokens returns registered tokens
    Given user "alice-test-user" has registered device token "active-token" on platform "Android"
    When user "alice-test-user" requests active device tokens via gRPC
    Then the response should contain token "active-token"

  @FEAT-047 @Query
  Scenario: GetActiveDeviceTokens does not return unregistered tokens
    Given user "alice-test-user" has registered device token "inactive-token" on platform "Android"
    And user "alice-test-user" has unregistered token "inactive-token"
    When user "alice-test-user" requests active device tokens via gRPC
    Then the response should not contain token "inactive-token"

  # Database Index Verification
  @FEAT-047 @DatabaseSchema
  Scenario: Database has required indexes for push token queries
    Then the PostgreSQL index "IX_DeviceTokens_UserId" should exist on "Notifications"."DeviceTokens"
