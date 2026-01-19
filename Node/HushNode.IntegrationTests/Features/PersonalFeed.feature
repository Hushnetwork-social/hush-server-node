@Integration
Feature: Personal Feed Creation
  As a user
  I want to register my identity
  So that I have a personal feed for my posts

  Background:
    Given a HushServerNode at block 1

  # TODO: This scenario times out on "a block is produced" - needs BlockProductionControl investigation.
  @ignore
  Scenario: User registration creates personal feed
    Given user "Alice" is not registered
    When Alice registers her identity via gRPC
    And a block is produced
    Then Alice should have a personal feed
    And Alice's display name should be "Alice"
