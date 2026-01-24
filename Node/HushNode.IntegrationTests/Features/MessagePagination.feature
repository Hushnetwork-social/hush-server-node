@Integration @EPIC-003 @FEAT-052
Feature: FEAT-052 Message Pagination
  As a client performing sync
  I want to paginate through messages
  So that I can efficiently sync large message histories

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  # ===== Basic Pagination =====

  @Pagination
  Scenario: Default limit returns max 100 messages
    Given the ChatFeed contains 150 messages
    When Alice requests messages with fetch_latest true via gRPC
    Then the response should contain at most 100 messages
    And the response has_more_messages should be true

  @Pagination
  Scenario: Custom limit is respected
    Given the ChatFeed contains 50 messages
    When Alice requests messages with fetch_latest true and limit 20 via gRPC
    Then the response should contain exactly 20 messages
    And the response has_more_messages should be true

  @Pagination
  Scenario: Limit larger than available returns all
    Given the ChatFeed contains 30 messages
    When Alice requests messages with fetch_latest true and limit 100 via gRPC
    Then the response should contain exactly 30 messages
    And the response has_more_messages should be false

  # ===== Fetch Latest Mode =====

  @FetchLatest
  Scenario: Fetch latest returns most recent messages
    Given the ChatFeed contains 150 messages
    When Alice requests messages with fetch_latest true and limit 50 via gRPC
    Then the response should contain exactly 50 messages
    And the messages should be ordered by block_index ascending

  @FetchLatest
  Scenario: Fetch latest with small limit
    Given the ChatFeed contains 100 messages
    When Alice requests messages with fetch_latest true and limit 10 via gRPC
    Then the response should contain exactly 10 messages
    And the response has_more_messages should be true

  # ===== Incremental Sync =====

  @IncrementalSync
  Scenario: Incremental sync returns new messages only
    Given the ChatFeed contains 30 messages
    And Alice records the current block height
    And another 20 messages are added to the ChatFeed
    When Alice requests messages since the recorded block via gRPC
    Then the response should contain at least 20 messages

  @IncrementalSync
  Scenario: Incremental sync with no new messages
    Given the ChatFeed contains 50 messages
    And Alice records the current block height
    When Alice requests messages since the recorded block via gRPC
    Then the response should contain exactly 0 messages
    And the response has_more_messages should be false

  # ===== Edge Cases =====

  @EdgeCase
  Scenario: Empty feed returns no messages
    Given the ChatFeed has no messages
    When Alice requests messages for the ChatFeed via gRPC
    Then the response should contain exactly 0 messages
    And the response has_more_messages should be false

  @EdgeCase
  Scenario: Pagination respects feed boundaries
    Given the ChatFeed contains 50 messages
    And a Charlie-Bob ChatFeed exists with 30 messages
    When Alice requests messages with fetch_latest true and limit 100 via gRPC
    Then the response should contain exactly 50 messages
    And all messages should belong to the ChatFeed with Bob

  # ===== Configuration =====

  @Configuration
  Scenario: Client limit cannot exceed server max
    Given the ChatFeed contains 150 messages
    When Alice requests messages with fetch_latest true and limit 500 via gRPC
    Then the response should contain at most 100 messages
