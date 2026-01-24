@Integration @EPIC-003 @FEAT-052
Feature: FEAT-052 GetMessageById
  As a client opening a deep link
  I want to fetch a specific message by ID
  So that I can display reply context or shared messages

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  # ===== Basic GetMessageById =====

  @GetMessageById
  Scenario: Fetch existing message by ID
    Given the ChatFeed contains message "msg-001" with content "Hello World" at block 10
    When Alice requests message by ID "msg-001" via gRPC
    Then the GetMessageById response should be successful
    And the GetMessageById response should include a valid block_index

  @GetMessageById
  Scenario: Fetch non-existent message returns error
    When Alice requests message by ID "non-existent-id" via gRPC
    Then the GetMessageById response should indicate failure
    And the GetMessageById error message should contain "not found"

  # ===== Security =====
  # Note: Feed content is encrypted with AES keys shared only with participants.
  # Server returns encrypted messages; only participants can decrypt.
  # This test verifies messages can be fetched but would be unreadable without the key.

  @GetMessageById @Security
  Scenario: Fetch message from another feed returns encrypted content
    Given user "Charlie" is registered with a personal feed
    And a Charlie-Bob ChatFeed exists with message "secret-msg"
    When Alice requests message by ID "secret-msg" from Charlie-Bob feed via gRPC
    Then the GetMessageById response should be successful

  # ===== Sender Information =====

  @GetMessageById
  Scenario: Fetch message returns sender information
    Given the ChatFeed contains message "msg-from-bob" sent by Bob with content "Hi Alice"
    When Alice requests message by ID "msg-from-bob" via gRPC
    Then the GetMessageById response should be successful
    And the GetMessageById response should include the sender public key for Bob
