@Integration @FEAT-094 @NON_E2E
Feature: FEAT-094 election lifecycle integration
  As a FEAT-094 maintainer
  I want the election lifecycle flow wired through the real node host
  So that the minimum owner workflow can be validated without broad browser execution

  @AT-PROC-U01 @AT-PROC-U02
  Scenario: Admin-only lifecycle roundtrip persists through gRPC reads
    Given FEAT-094 election integration services are available
    When the owner creates an admin-only election draft through blockchain submission
    And the owner updates the election draft title to "Board Election Final"
    And the owner checks open readiness for the election
    And the owner opens the election through blockchain submission
    And the owner reloads the election through gRPC
    And the owner closes the election through blockchain submission
    And the owner finalizes the election through blockchain submission
    Then the election lifecycle should progress through "Draft", "Open", "Closed", and "Finalized"
    And the owner dashboard should include the election
    And the frozen policy and warning acknowledgement should remain visible after reload
    And the boundary artifacts should include open, close, and finalize records

  @AT-PROC-I01 @AT-PROC-U01
  Scenario: Immutable policy updates are rejected after open
    Given FEAT-094 election integration services are available
    And the owner has an open admin-only election through blockchain submission
    When the owner attempts to change the binding status after open
    Then the immutable update transaction should be rejected before indexing
    And the open-time binding status should remain "Binding"

  @AT-PROC-I01 @AT-PROC-U02
  Scenario: Trustee-threshold open remains blocked while the key ceremony boundary is incomplete
    Given FEAT-094 election integration services are available
    When the owner creates a trustee-threshold election draft through blockchain submission
    And the owner invites trustee "Bob" through blockchain submission
    And trustee "Bob" accepts the invitation through gRPC
    And the owner invites trustee "Charlie" through blockchain submission
    And the owner checks open readiness for the trustee-threshold election
    And the owner attempts to open the trustee-threshold election through blockchain submission
    Then the readiness response should include the missing ready-ceremony blocker
    And the trustee-threshold open transaction should be rejected before the MemPool

  @FEAT-096 @AT-GOV-096-OPEN
  Scenario: Governed open blocks further draft edits and opens at trustee threshold
    Given FEAT-094 election integration services are available
    When the owner creates a trustee-threshold election draft through blockchain submission
    And the owner prepares a ready trustee ceremony through gRPC
    And the owner starts an "open" governed proposal through blockchain submission
    And the owner attempts to update the trustee-threshold draft title to "Governed Referendum Revised" while a governed open proposal is pending
    Then the pending governed open should block further draft changes
    When trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should execute and transition the election to "Open"

  @FEAT-096 @AT-GOV-096-CLOSE
  Scenario: Governed close locks vote acceptance immediately and closes at threshold
    Given FEAT-094 election integration services are available
    And the owner has an open trustee-threshold election through governed approval blockchain submission
    When the owner starts an "close" governed proposal through blockchain submission
    Then the governed proposal should remain pending for "close" while the election stays "Open"
    And vote acceptance should be locked immediately on the election
    When trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should execute and transition the election to "Closed"

  @FEAT-096 @AT-GOV-096-RETRY
  Scenario: Owner can retry a failed governed proposal after the blocking state is repaired
    Given FEAT-094 election integration services are available
    When the owner creates a trustee-threshold election draft through blockchain submission
    And the owner prepares a ready trustee ceremony through gRPC
    And the owner starts an "open" governed proposal through blockchain submission
    And the integration test forces the election into a stale "Closed" state before the governed proposal executes
    And trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should record an execution failure for "open"
    When the integration test restores the election to the "Draft" state for governed retry
    And the owner retries the governed proposal execution through blockchain submission
    Then the governed proposal should execute and transition the election to "Open"
