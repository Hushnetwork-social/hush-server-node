@Integration @FEAT-094 @NON_E2E
Feature: FEAT-094 election lifecycle integration
  As a FEAT-094 maintainer
  I want the election lifecycle flow wired through the real node host
  So that the minimum owner workflow can be validated without broad browser execution

  @AT-PROC-U01 @AT-PROC-U02
  Scenario: Admin-only lifecycle roundtrip persists through gRPC reads
    Given FEAT-094 election integration services are available
    When the owner creates an admin-only election draft through gRPC
    And the owner updates the election draft title to "Board Election Final"
    And the owner checks open readiness for the election
    And the owner opens the election through gRPC
    And the owner reloads the election through gRPC
    And the owner closes the election through gRPC
    And the owner finalizes the election through gRPC
    Then the election lifecycle should progress through "Draft", "Open", "Closed", and "Finalized"
    And the owner dashboard should include the election
    And the frozen policy and warning acknowledgement should remain visible after reload
    And the boundary artifacts should include open, close, and finalize records

  @AT-PROC-I01 @AT-PROC-U01
  Scenario: Immutable policy updates are rejected after open
    Given FEAT-094 election integration services are available
    And the owner has an open admin-only election through gRPC
    When the owner attempts to change the binding status after open
    Then the immutable update should be rejected through gRPC
    And the open-time binding status should remain "Binding"
