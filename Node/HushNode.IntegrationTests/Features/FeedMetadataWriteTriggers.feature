@Integration
Feature: FEAT-065 Feed Metadata Write Triggers
  As a cache system
  I want transaction handlers to write full metadata to Redis
  So that the 3-second sync hot path can serve from cache without PostgreSQL

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  # F6-003: Chat feed creation populates full metadata with resolved titles
  @FEAT-065 @F6-003
  Scenario: Chat feed creation populates both participants' feed_meta with resolved titles
    When Alice requests a ChatFeed with Bob via gRPC
    And a block is produced
    Then Alice's feed_meta Hash should contain the ChatFeed with title "Bob"
    And Bob's feed_meta Hash should contain the ChatFeed with title "Alice"
    And Alice's feed_meta entry should have type Chat and correct participants

  # F6-002: New message updates lastBlockIndex
  @FEAT-065 @F6-002
  Scenario: New message updates lastBlockIndex in participants' feed_meta Hashes
    Given Alice has a ChatFeed with Bob
    When Alice sends message "Hello Bob!" to the ChatFeed via gRPC
    And a block is produced
    Then Alice's feed_meta Hash entry for the ChatFeed should have an updated lastBlockIndex
    And Bob's feed_meta Hash entry for the ChatFeed should have an updated lastBlockIndex

  # F6-008: Identity display name change updates E2 cache
  @FEAT-065 @F6-008
  Scenario: Identity name change updates display name cache
    Given Alice has a ChatFeed with Bob
    When Bob changes display name to "Robert" via gRPC
    And a block is produced
    Then the identity display names Hash should contain "Robert" for Bob's address

  # F6-008b: Identity name change cascades to Personal feed title
  @FEAT-065 @F6-008
  Scenario: Identity name change updates Personal feed title in own feed_meta
    Given Alice has a ChatFeed with Bob
    And Bob's feed_meta Hash is flushed
    And GetFeedsForAddress is called for Bob
    When Bob changes display name to "Robert" via gRPC
    And a block is produced
    Then Bob's feed_meta Hash should contain his Personal feed with title "Robert (YOU)"

  # F6-008c: Identity name change cascades ChatFeed title to other participant
  @FEAT-065 @F6-008
  Scenario: Identity name change cascades ChatFeed title to other participant
    Given Alice has a ChatFeed with Bob
    And Alice's feed_meta Hash is flushed
    And GetFeedsForAddress is called for Alice
    When Bob changes display name to "Robert" via gRPC
    And a block is produced
    Then Alice's feed_meta Hash should contain the ChatFeed with title "Robert"

  # F6-008d: Identity name change verified via gRPC response
  @FEAT-065 @F6-008
  Scenario: Identity name change reflected in GetFeedsForAddress gRPC response
    Given Alice has a ChatFeed with Bob
    And Alice's feed_meta Hash is flushed
    And GetFeedsForAddress is called for Alice
    When Bob changes display name to "Robert" via gRPC
    And a block is produced
    Then GetFeedsForAddress for Alice should return the ChatFeed with title "Robert"

  # Group title change cascades to all participants' feed_meta
  @FEAT-065 @GroupTitleCascade
  Scenario: Group title change updates all participants' feed_meta
    Given Alice has created a public group feed "Team Alpha"
    And Bob joins the public group "Team Alpha" via gRPC
    And a block is produced
    And GetFeedsForAddress is called for Alice
    And GetFeedsForAddress is called for Bob
    When Alice changes group "Team Alpha" title to "Team Beta" via gRPC
    Then Alice's feed_meta Hash should contain group "Team Alpha" with title "Team Beta"
    And Bob's feed_meta Hash should contain group "Team Alpha" with title "Team Beta"

  # Group title change verified via gRPC response
  @FEAT-065 @GroupTitleCascade
  Scenario: Group title change reflected in GetFeedsForAddress gRPC response
    Given Alice has created a public group feed "Team Alpha"
    And Bob joins the public group "Team Alpha" via gRPC
    And a block is produced
    And GetFeedsForAddress is called for Alice
    When Alice changes group "Team Alpha" title to "Team Beta" via gRPC
    Then GetFeedsForAddress for Alice should return group feed with title "Team Beta"

  # F6-003 supplementary: Read path populates cache on miss
  @FEAT-065 @F6-003
  Scenario: GetFeedsForAddress populates feed_meta cache on miss
    Given Alice has a ChatFeed with Bob
    When Alice's feed_meta Hash is flushed
    And GetFeedsForAddress is called for Alice
    Then Alice's feed_meta Hash should be repopulated with the ChatFeed
