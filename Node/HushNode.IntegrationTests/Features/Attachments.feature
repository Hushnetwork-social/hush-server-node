@Integration @Attachments
Feature: Attachment Storage Infrastructure
  As a user
  I want to send file attachments with messages
  So that I can share media and documents in chat feeds

  Background:
    Given a HushServerNode at block 5
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  @F2-001 @F2-002 @F2-004
  Scenario: Complete attachment roundtrip - upload, store, download
    When Alice sends message "Check this photo" with a 10KB image attachment to the ChatFeed
    And a block is produced
    Then the message should contain attachment metadata with id, hash, type, and size
    And the encrypted attachment should be stored in PostgreSQL
    And Bob should be able to download the attachment via gRPC streaming
    And the downloaded bytes should match the uploaded bytes

  @F2-005
  Scenario: Thumbnail download separate from original
    When Alice sends message "See this" with an attachment that has both original and thumbnail
    And a block is produced
    Then downloading with thumbnail_only should return only the thumbnail bytes
    And downloading without thumbnail_only should return only the original bytes
    And the thumbnail should be smaller than the original

  @F2-003
  Scenario: Temp file deleted after indexing
    When Alice sends message "File attached" with a 5KB attachment to the ChatFeed
    Then a temp file should exist for the attachment
    When a block is produced
    Then the temp file should no longer exist

  @F2-010
  Scenario: Temp file cleaned up on validation rejection
    When Alice sends a message with mismatched blob and metadata count
    Then the submission should be rejected
    And no temp files should exist for the attachment

  @F2-008
  Scenario: Attachment under 25MB is accepted
    When Alice sends message "Small file" with a 1KB attachment to the ChatFeed
    And a block is produced
    Then Alice should see the message with attachment metadata in the ChatFeed

  @F2-008
  Scenario: Attachment over 25MB is rejected
    When Alice sends a message with a 26MB attachment to the ChatFeed
    Then the submission should be rejected with a size limit error

  @F2-009
  Scenario: 5 attachments are accepted
    When Alice sends message "Many files" with 5 attachments to the ChatFeed
    And a block is produced
    Then Alice should see the message with 5 attachment references

  @F2-009
  Scenario: 6 attachments are rejected
    When Alice sends a message with 6 attachments to the ChatFeed
    Then the submission should be rejected with a count limit error

  @F2-013
  Scenario: Backward compatibility - text-only message unchanged
    When Alice sends message "Just text, no files" to the ChatFeed via gRPC
    And a block is produced
    Then Bob should see the message "Just text, no files" in the ChatFeed
    And the message should have an empty attachments list
