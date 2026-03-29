@Integration @FEAT-094 @NON_E2E
Feature: FEAT-094 election lifecycle integration
  As a FEAT-094 maintainer
  I want the election lifecycle flow wired through the real node host
  So that the minimum owner workflow can be validated without broad browser execution

  @AT-PROC-U01 @AT-PROC-U02
  Scenario: Admin-only lifecycle roundtrip persists through gRPC reads
    Given FEAT-094 election integration services are available
    When the owner creates an admin-only election draft through blockchain submission
    And the owner imports the default election roster through blockchain submission
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

  @AT-PROC-I01
  Scenario: Legacy plaintext election writes are rejected before MemPool admission
    Given FEAT-094 election integration services are available
    When the owner creates an admin-only election draft through blockchain submission
    When the owner submits a legacy plaintext open election transaction
    Then the legacy plaintext election transaction should be rejected before the MemPool
    And the election should remain in "Draft"

  @AT-PROC-I01 @AT-PROC-U02
  Scenario: Trustee-threshold open remains blocked while the key ceremony boundary is incomplete
    Given FEAT-094 election integration services are available
    When the owner creates a trustee-threshold election draft through blockchain submission
    And the owner imports the default election roster through blockchain submission
    And the owner invites trustee "Bob" through blockchain submission
    And trustee "Bob" accepts the invitation through blockchain submission
    And the owner invites trustee "Charlie" through blockchain submission
    And the owner checks open readiness for the trustee-threshold election
    And the owner attempts to open the trustee-threshold election through blockchain submission
    Then the readiness response should include the missing ready-ceremony blocker
    And the trustee-threshold open transaction should be rejected before the MemPool

  @FEAT-095 @AT-PROC-U03 @AT-PROC-U04 @AT-PROC-U11 @NON_E2E
  Scenario: Eligibility claim and late activation surface through restricted and voter views
    Given FEAT-094 election integration services are available
    When the owner creates a late-activation admin-only election draft through blockchain submission
    And the owner imports the default election roster through blockchain submission
    And voter "Bob" claims roster entry "voter-bob" with temporary verification code through blockchain submission
    And the owner checks open readiness for the election
    And the owner opens the election through blockchain submission
    And the owner activates roster entry "voter-bob" through blockchain submission
    And the actor "Bob" requests the election eligibility view through gRPC
    Then the eligibility view should show actor role "EligibilityActorLinkedVoter"
    And the eligibility view should expose temporary verification code "1111"
    And the eligibility self row should show organization voter "voter-bob" as "VotingRightActive" with participation "ParticipationDidNotVote"
    When the actor "Alice" requests the election eligibility view through gRPC
    Then the eligibility view should show actor role "EligibilityActorOwner"
    And the owner eligibility summary should report 3 rostered voters, 1 linked voters, and 1 activation events
    And the restricted eligibility roster should include 3 entries

  @FEAT-096 @AT-GOV-096-OPEN
  Scenario: Governed open blocks further draft edits and opens at trustee threshold
    Given FEAT-094 election integration services are available
    When the owner creates a trustee-threshold election draft through blockchain submission
    And the owner imports the default election roster through blockchain submission
    And the owner prepares a ready trustee ceremony through blockchain submission
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
    And the owner imports the default election roster through blockchain submission
    And the owner prepares a ready trustee ceremony through blockchain submission
    And the owner starts an "open" governed proposal through blockchain submission
    And the integration test forces the election into a stale "Closed" state before the governed proposal executes
    And trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should record an execution failure for "open"
    When the integration test restores the election to the "Draft" state for governed retry
    And the owner retries the governed proposal execution through blockchain submission
    Then the governed proposal should execute and transition the election to "Open"

  @FEAT-099 @AT-PROC-U06 @AT-PROC-U08 @AT-PROC-U12 @AT-PROC-U13 @AT-PROC-I08 @NON_E2E
  Scenario: Accepted cast moves from mempool pending to committed receipt state without durable extra artifacts
    Given FEAT-094 election integration services are available
    And the owner has an open trustee-threshold election through governed approval blockchain submission
    When voter "Alice" claims roster entry "voter-alice" with temporary verification code through blockchain submission
    And voter "Alice" registers voting commitment "alice-commitment-v1" through blockchain submission
    Then the voting view should show commitment as registered
    And the voting view should show personal participation "ParticipationDidNotVote"
    When voter "Alice" submits ballot cast with idempotency key "alice-cast-001" without block production
    And the actor "Alice" requests the election voting view with submission idempotency key "alice-cast-001" through gRPC
    Then the voting view should show submission status "VotingSubmissionStatusStillProcessing"
    When voter "Alice" retries ballot cast with idempotency key "alice-cast-001" before block production
    Then the last blockchain submission should be rejected with validation code "election_cast_still_processing"
    When the pending cast block is produced
    And the actor "Alice" requests the election voting view with submission idempotency key "alice-cast-001" through gRPC
    Then the voting view should show submission status "VotingSubmissionStatusAlreadyUsed"
    And the voting view should show personal participation "ParticipationCountedAsVoted"
    And the voting view should expose acceptance receipt metadata
    And only the committed FEAT-099 acceptance artifacts should remain for actor "Alice" and idempotency key "alice-cast-001"
    When voter "Alice" retries ballot cast with idempotency key "alice-cast-001" after block production
    Then the last blockchain submission should be rejected with validation code "election_cast_already_used"

  @FEAT-099 @AT-PROC-U07 @NON_E2E
  Scenario: Persisted close rejects later FEAT-099 cast attempts without committing vote artifacts
    Given FEAT-094 election integration services are available
    And the owner has an open trustee-threshold election through governed approval blockchain submission
    When voter "Alice" claims roster entry "voter-alice" with temporary verification code through blockchain submission
    And voter "Alice" registers voting commitment "alice-commitment-close-v1" through blockchain submission
    And the owner starts an "close" governed proposal through blockchain submission
    And trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    When voter "Alice" submits ballot cast with idempotency key "alice-close-001" through blockchain submission
    Then the last blockchain submission should be rejected with validation code "election_cast_close_persisted"
    When the actor "Alice" requests the election voting view through gRPC
    Then the voting view should show personal participation "ParticipationDidNotVote"
    And the voting view should not expose acceptance receipt metadata

  @FEAT-100 @AT-PROC-I04 @AT-PROC-I05 @AT-PROC-I06 @NON_E2E
  Scenario: Close drain publishes queued ballots and seals tally_ready after close-counting shares
    Given FEAT-094 election integration services are available
    And the owner has an open trustee-threshold election through governed approval blockchain submission
    When voter "Alice" claims roster entry "voter-alice" with temporary verification code through blockchain submission
    And voter "Alice" registers voting commitment "alice-commitment-feat100-v1" through blockchain submission
    And voter "Alice" submits ballot cast with idempotency key "alice-feat100-001" through blockchain submission
    And the owner starts an "close" governed proposal through blockchain submission
    And trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should execute and transition the election to "Closed"
    And the election closed progress status should be "ClosedProgressWaitingForTrusteeShares"
    And the election should not expose a tally-ready boundary yet
    And the close workflow should open a bound close-counting session while the election stays "Closed"
    When trustee "Bob" submits a bound finalization share through blockchain submission
    And trustee "Charlie" submits a bound finalization share through blockchain submission
    And trustee "Delta" submits a bound finalization share through blockchain submission
    Then the election should expose a tally-ready boundary after close drain
    And the tally-ready boundary should reconcile 1 accepted ballots and 1 published ballots

  @FEAT-098 @AT-PROC-I03 @NON_E2E
  Scenario: Close-counting binds one exact aggregate target and rejects single-ballot release
    Given FEAT-094 election integration services are available
    And the owner has an open trustee-threshold election through governed approval blockchain submission
    When voter "Alice" claims roster entry "voter-alice" with temporary verification code through blockchain submission
    And voter "Alice" registers voting commitment "alice-commitment-feat098-v1" through blockchain submission
    And voter "Alice" submits ballot cast with idempotency key "alice-feat098-001" through blockchain submission
    And the owner starts an "close" governed proposal through blockchain submission
    And trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should execute and transition the election to "Closed"
    And the election closed progress status should be "ClosedProgressWaitingForTrusteeShares"
    And the close workflow should open a bound close-counting session while the election stays "Closed"
    When trustee "Bob" submits a single-ballot finalization share through blockchain submission
    Then the finalization share log should record rejection code "SINGLE_BALLOT_RELEASE_FORBIDDEN" for trustee "Bob"
    And the election should remain in "Closed"
    When trustee "Bob" submits a bound finalization share through blockchain submission
    And trustee "Charlie" submits a bound finalization share through blockchain submission
    Then the finalization session should remain waiting for 2 accepted shares
    When trustee "Delta" submits a bound finalization share through blockchain submission
    Then the close-counting release evidence should record 3 accepted trustee shares
    And the election should expose a tally-ready boundary after close drain
    And the election should remain in "Closed"

  @FEAT-101 @AT-PROC-I04 @AT-PROC-I05 @NON_E2E
  Scenario: Close counting publishes unofficial result and finalize copies the official result
    Given FEAT-094 election integration services are available
    And the owner has an open trustee-threshold election through governed approval blockchain submission
    When voter "Alice" claims roster entry "voter-alice" with temporary verification code through blockchain submission
    And voter "Alice" registers voting commitment "alice-commitment-feat101-v1" through blockchain submission
    And voter "Alice" submits ballot cast with idempotency key "alice-feat101-001" through blockchain submission
    And the owner starts an "close" governed proposal through blockchain submission
    And trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should execute and transition the election to "Closed"
    And the election closed progress status should be "ClosedProgressWaitingForTrusteeShares"
    And the close workflow should open a bound close-counting session while the election stays "Closed"
    When trustee "Bob" submits a bound finalization share through blockchain submission
    And trustee "Charlie" submits a bound finalization share through blockchain submission
    And trustee "Delta" submits a bound finalization share through blockchain submission
    Then the election should expose a tally-ready boundary after close drain
    And the election result view for actor "Alice" should expose participant-encrypted unofficial results
    And the unofficial result should report 1 total voted, 2 eligible to vote, 1 did not vote, and 0 blank
    And the unofficial result should include all named options
    When the owner starts a "finalize" governed proposal through blockchain submission
    Then the governed proposal should remain pending for "finalize" while the election stays "Closed"
    When trustee "Bob" approves the governed proposal through blockchain submission
    And trustee "Charlie" approves the governed proposal through blockchain submission
    And trustee "Delta" approves the governed proposal through blockchain submission
    Then the governed proposal should execute and transition the election to "Finalized"
    And the official result should copy the unofficial result for actor "Alice"
    And the boundary artifacts should include open, close, and finalize records
