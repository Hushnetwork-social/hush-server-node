@Integration @FEAT-063
Feature: Cross-Device Read Sync
  As a user with multiple devices
  I want read status to sync with upToBlockIndex watermark
  So that receiving devices can calculate correct unread counts

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  @F3-INT-001
  Scenario: MessagesRead event includes upToBlockIndex watermark
    Given "Alice" subscribes to notification events via gRPC
    When Alice marks the feed as read up to block 800
    Then "Alice" receives a MessagesRead event within 5000ms
    And the event contains the correct feedId
    And the event contains upToBlockIndex = 800

  @F3-INT-002
  Scenario: MessagesRead event with block 0 (mark all as read)
    Given "Alice" subscribes to notification events via gRPC
    When Alice marks the feed as read up to block 0
    Then "Alice" receives a MessagesRead event within 5000ms
    And the event contains upToBlockIndex = 0

  @F3-INT-003
  Scenario: UNREAD_COUNT_SYNC event received on initial connection
    When "Alice" subscribes to notification events via gRPC
    Then "Alice" receives UNREAD_COUNT_SYNC on first connection
