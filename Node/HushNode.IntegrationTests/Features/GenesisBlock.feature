@Integration
Feature: Genesis Block Creation
  As the network operator
  I want the genesis block to be created correctly
  So that the blockchain has a valid starting point

  Scenario: Genesis block is created on fresh node startup
    Given a fresh HushServerNode without any blocks
    And BlockProducer credentials are configured
    When a block is produced
    Then the genesis block should exist at index 1
    And the BlockProducer should have 5 HUSH balance
    And the BlockProducer should have a personal feed
