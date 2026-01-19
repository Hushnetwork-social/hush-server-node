@Integration
Feature: Chat Feed and Messaging
  As a user
  I want to create a chat with another user
  So that we can exchange messages

  Background:
    Given a HushServerNode at block 5
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  @Walkthrough
  Scenario: Chat feed creation
    When Alice requests a ChatFeed with Bob via gRPC
    And a block is produced
    Then Alice should have a ChatFeed with Bob
    And Bob should have a ChatFeed with Alice

  Scenario: Sending a message in chat
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello Bob!" to the ChatFeed via gRPC
    And a block is produced
    Then Bob should see the message "Hello Bob!" in the ChatFeed
    And Alice should see the message "Hello Bob!" in the ChatFeed
