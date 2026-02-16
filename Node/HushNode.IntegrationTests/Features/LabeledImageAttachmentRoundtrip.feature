@Integration @Attachments @LabeledImageRoundtrip
Feature: Labeled Image Attachment Roundtrip
  As an E2E test infrastructure
  I want to verify that labeled test images survive the full gRPC upload/download pipeline
  So that E2E tests can trust that the correct image arrived at the receiver

  Background:
    Given a HushServerNode at block 5
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed
    And Alice has a ChatFeed with Bob

  @F3-TWIN-001
  Scenario: Labeled PNG with thumbnail survives upload-download roundtrip
    When Alice sends a labeled image 1 for "Bob" with thumbnail to the ChatFeed
    And a block is produced
    Then Bob should download the original and it should match the uploaded encrypted bytes
    And Bob should download the thumbnail and it should match the uploaded encrypted thumbnail bytes
    And the downloaded original should be a valid PNG when decrypted
    And the downloaded thumbnail should be a valid PNG when decrypted
