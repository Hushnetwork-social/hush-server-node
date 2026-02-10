@Integration @EPIC-004 @FEAT-059
Feature: FEAT-059 Per-Feed Pagination API
  As a client developer
  I want to fetch messages for a specific feed with cursor pagination
  So that I can implement scroll-based prefetch

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  # ===== Basic Per-Feed Pagination =====

  @PerFeedPagination
  Scenario: Per-feed pagination returns messages for specific feed
    Given the ChatFeed contains 50 messages
    When Alice calls GetFeedMessagesById for the ChatFeed via gRPC
    Then the per-feed response should contain exactly 50 messages
    And the per-feed response has_more_messages should be false
    And all per-feed messages should belong to the ChatFeed

  @PerFeedPagination
  Scenario: Per-feed pagination with cursor returns older messages
    Given the ChatFeed contains 150 messages
    # First request: get newest 100 messages
    When Alice calls GetFeedMessagesById for the ChatFeed via gRPC
    Then the per-feed response should contain exactly 100 messages
    And the per-feed response has_more_messages should be true
    And Alice records the per-feed oldest_block_index from the response
    # Second request: get older messages before the oldest
    When Alice calls GetFeedMessagesById with beforeBlockIndex via gRPC
    Then the per-feed response should contain exactly 50 messages
    And the per-feed response has_more_messages should be false

  @PerFeedPagination
  Scenario: Per-feed pagination with custom limit is respected
    Given the ChatFeed contains 50 messages
    When Alice calls GetFeedMessagesById with limit 20 via gRPC
    Then the per-feed response should contain exactly 20 messages
    And the per-feed response has_more_messages should be true

  # ===== Multi-Page Pagination =====

  @PerFeedPagination @MultiPage
  Scenario: Three requests to paginate 250 messages in a specific feed
    Given the ChatFeed contains 250 messages
    # First request: get newest 100
    When Alice calls GetFeedMessagesById for the ChatFeed via gRPC
    Then the per-feed response should contain exactly 100 messages
    And the per-feed response has_more_messages should be true
    And Alice records the per-feed oldest_block_index from the response
    # Second request: get next 100 older
    When Alice calls GetFeedMessagesById with beforeBlockIndex via gRPC
    Then the per-feed response should contain exactly 100 messages
    And the per-feed response has_more_messages should be true
    And Alice records the per-feed oldest_block_index from the response
    # Third request: get remaining 50
    When Alice calls GetFeedMessagesById with beforeBlockIndex via gRPC
    Then the per-feed response should contain exactly 50 messages
    And the per-feed response has_more_messages should be false

  # ===== Authorization =====

  @PerFeedPagination @Authorization
  Scenario: Per-feed pagination denies access for non-participant
    Given the ChatFeed contains 20 messages
    And user "Charlie" is registered with a personal feed
    When Charlie calls GetFeedMessagesById for the Alice-Bob ChatFeed via gRPC
    Then the per-feed response should contain exactly 0 messages
    And the per-feed response has_more_messages should be false

  # ===== Edge Cases =====

  @PerFeedPagination @EdgeCase
  Scenario: Per-feed pagination for empty feed returns no messages
    When Alice calls GetFeedMessagesById for the ChatFeed via gRPC
    Then the per-feed response should contain exactly 0 messages
    And the per-feed response has_more_messages should be false

  @PerFeedPagination @EdgeCase
  Scenario: Per-feed pagination with non-existent feed returns no messages
    When Alice calls GetFeedMessagesById for a non-existent feed via gRPC
    Then the per-feed response should contain exactly 0 messages
    And the per-feed response has_more_messages should be false

  @PerFeedPagination @EdgeCase
  Scenario: Per-feed pagination respects feed isolation
    Given the ChatFeed contains 30 messages
    And a Charlie-Bob ChatFeed exists with 50 messages
    When Alice calls GetFeedMessagesById for the ChatFeed via gRPC
    Then the per-feed response should contain exactly 30 messages
    And all per-feed messages should belong to the ChatFeed

  # ===== Response Fields =====

  @PerFeedPagination @ResponseFields
  Scenario: Per-feed pagination returns correct oldest and newest block indexes
    Given the ChatFeed contains 50 messages
    When Alice calls GetFeedMessagesById for the ChatFeed via gRPC
    Then the per-feed response oldest_block_index should be less than newest_block_index
    And the per-feed response newest_block_index should be the block of the newest message
