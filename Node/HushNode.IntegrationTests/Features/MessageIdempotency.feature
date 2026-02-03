@Integration @FEAT-057
Feature: Message Idempotency
  As a server
  I want to detect duplicate message submissions
  So that clients can safely retry without creating duplicate messages

  Background:
    Given a HushServerNode at block 5
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  @F1-004 @Critical
  Scenario: F1-004 - MemPool to DB transition (ACCEPTED -> PENDING -> ALREADY_EXISTS)
    # First submission - new message
    Given Alice creates a message with a specific message ID "idempotency-test-001"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"
    And the response should be successful

    # Second submission - message still in MemPool (no block produced yet)
    When Alice submits the same message again via gRPC
    Then the response status should be "PENDING"
    And the response should be successful

    # Block is created - message moves to database
    When a block is produced

    # Third submission - message now in database
    When Alice submits the same message again via gRPC
    Then the response status should be "ALREADY_EXISTS"
    And the response should be successful

  @F1-006
  Scenario: F1-006 - MemPool cleanup on block creation
    # Submit multiple messages with known IDs
    Given Alice creates a message with a specific message ID "cleanup-test-001"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"

    Given Alice creates a message with a specific message ID "cleanup-test-002"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"

    # Both messages should be pending in MemPool
    When Alice submits message "cleanup-test-001" again via gRPC
    Then the response status should be "PENDING"

    When Alice submits message "cleanup-test-002" again via gRPC
    Then the response status should be "PENDING"

    # Block is created - all messages move to database, tracking cleaned up
    When a block is produced

    # After block, messages should be in database
    When Alice submits message "cleanup-test-001" again via gRPC
    Then the response status should be "ALREADY_EXISTS"

    When Alice submits message "cleanup-test-002" again via gRPC
    Then the response status should be "ALREADY_EXISTS"

  @NonFeedMessage
  Scenario: Non-FeedMessage transactions skip idempotency check
    # Identity registration is not a FeedMessage - should always be accepted or rejected normally
    Given user "Charlie" is not registered
    When Charlie registers his identity via gRPC
    Then the response status should be "ACCEPTED"
    And the response should be successful
