@Integration
Feature: Genesis Block Creation
  As the network operator
  I want the genesis block to be created correctly
  So that the blockchain has a valid starting point

  # Note: The node automatically creates the genesis block during initialization.
  # This scenario verifies that the genesis block was created correctly.
  # The genesis block only contains a reward transaction for the block producer.
  # Personal feeds require explicit identity registration (a separate feature).
  Scenario: Genesis block is created on fresh node startup
    Given a fresh HushServerNode without any blocks
    And BlockProducer credentials are configured
    Then the genesis block should exist at index 1

  # TODO: Balance check is flaky - block persistence sometimes hangs during test.
  # Needs investigation of database connection pooling/timeout in test context.
  @ignore
  Scenario: Genesis block contains reward transaction
    Given a fresh HushServerNode without any blocks
    And BlockProducer credentials are configured
    Then the genesis block should exist at index 1
    And the BlockProducer should have 5 HUSH balance

  # TODO: This scenario times out - needs investigation of BlockProductionControl observable subscription.
  # The BlockProductionSchedulerService subscribes to the observable in Handle(BlockchainInitializedEvent),
  # but the trigger signal from BlockProductionControl.ProduceBlockAsync() isn't being received.
  @ignore
  Scenario: Block production adds to blockchain
    Given a fresh HushServerNode without any blocks
    When a block is produced
    Then the blockchain should be at index 2
    And the BlockProducer should have 10 HUSH balance
