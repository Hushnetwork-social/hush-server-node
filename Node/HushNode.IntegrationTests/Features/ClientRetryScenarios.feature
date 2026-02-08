@Integration @FEAT-058
Feature: Client Retry Scenarios
  As a client application implementing message retry logic
  I want the server to return correct TransactionStatus values
  So that I can implement reliable message retry logic

  Background:
    Given a HushServerNode at block 5
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  # Cross-Feature Test CF-001
  # This test verifies that when a client retries a message that was already confirmed,
  # the server returns ALREADY_EXISTS so the client can mark it as confirmed.
  @CF-001 @Critical
  Scenario: CF-001 - Retry receives ALREADY_EXISTS after message confirmed in database
    Given Alice creates a message with a specific message ID "cf001-client-retry"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"
    And the response should be successful

    # Block is produced - message moves from MemPool to database
    When a block is produced

    # Client retries (simulating network timeout where client didn't receive response)
    When Alice submits the same message again via gRPC
    Then the response status should be "ALREADY_EXISTS"
    And the response should be successful

  # Cross-Feature Test CF-002
  # This test verifies that when a client retries while message is still in MemPool,
  # the server returns PENDING so the client knows to keep waiting.
  @CF-002
  Scenario: CF-002 - Retry receives PENDING when message still in MemPool
    Given Alice creates a message with a specific message ID "cf002-pending-retry"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"
    And the response should be successful

    # Quick retry - message still in MemPool (no block produced yet)
    When Alice submits the same message again via gRPC
    Then the response status should be "PENDING"
    And the response should be successful

  # Test that multiple retries before block production all return PENDING
  @Retry-Progression
  Scenario: Multiple retries show correct status progression from PENDING to ALREADY_EXISTS
    Given Alice creates a message with a specific message ID "progression-multi-retry"

    # First attempt - ACCEPTED (new message)
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"

    # Second attempt while in MemPool - PENDING
    When Alice submits the same message again via gRPC
    Then the response status should be "PENDING"

    # Third attempt still in MemPool - PENDING
    When Alice submits the same message again via gRPC
    Then the response status should be "PENDING"

    # Block is produced - message committed to database
    When a block is produced

    # Fourth attempt after block - ALREADY_EXISTS
    When Alice submits the same message again via gRPC
    Then the response status should be "ALREADY_EXISTS"

  # Test concurrent/near-simultaneous duplicate submissions
  @Concurrent-Duplicates
  Scenario: Near-simultaneous duplicate submissions handled correctly
    Given Alice creates a message with a specific message ID "concurrent-dup-001"

    # First submission - ACCEPTED
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"

    # Immediate resubmission - simulates concurrent request
    When Alice submits the same message again via gRPC
    Then the response status should be "PENDING"

  # Test that group feed messages work the same as chat feed messages
  @Group-Feed-Retry
  Scenario: Group feed message retry returns ALREADY_EXISTS after confirmation
    Given Alice has created a group feed "RetryTestGroup"
    And Alice creates a group message with ID "group-retry-001" for "RetryTestGroup"

    When Alice submits the group message to "RetryTestGroup" via gRPC
    Then the response status should be "ACCEPTED"
    And the response should be successful

    When a block is produced

    When Alice submits the same group message again via gRPC
    Then the response status should be "ALREADY_EXISTS"
    And the response should be successful

  # Test that group feed message retry while in MemPool returns PENDING
  @Group-Feed-Pending
  Scenario: Group feed message retry returns PENDING when still in MemPool
    Given Alice has created a group feed "PendingTestGroup"
    And Alice creates a group message with ID "group-pending-001" for "PendingTestGroup"

    When Alice submits the group message to "PendingTestGroup" via gRPC
    Then the response status should be "ACCEPTED"
    And the response should be successful

    # Immediate retry without block production
    When Alice submits the same group message again via gRPC
    Then the response status should be "PENDING"
    And the response should be successful

  # Test Chat feed specific retry (verification for completeness)
  @Chat-Feed-Retry
  Scenario: Chat feed message retry after block returns ALREADY_EXISTS
    Given Alice creates a message with a specific message ID "chat-explicit-retry"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"

    When a block is produced

    When Alice submits the same message again via gRPC
    Then the response status should be "ALREADY_EXISTS"
    And the response should be successful

  # Test that different message IDs are treated as new messages
  @Different-Message-Ids
  Scenario: Different message IDs are accepted independently
    Given Alice creates a message with a specific message ID "unique-msg-001"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"

    Given Alice creates a message with a specific message ID "unique-msg-002"
    When Alice submits the message to the ChatFeed via gRPC
    Then the response status should be "ACCEPTED"

    When a block is produced

    # Both messages should be in database now
    When Alice submits message "unique-msg-001" again via gRPC
    Then the response status should be "ALREADY_EXISTS"

    When Alice submits message "unique-msg-002" again via gRPC
    Then the response status should be "ALREADY_EXISTS"
