@HushVoting @HV-E2E @E2E @HV-IDENTITY-CREATE @HV-FEAT-007
@FEAT-007
Feature: Identity create — staging, submission, confirmation, delay, correction, reset
  Covers HV-ID-CREATE-STAGE, HV-ID-CREATE-SUBMIT, HV-ID-CREATE-CONFIRM,
  HV-ID-CREATE-DELAY, HV-ID-CREATE-CORRECT, HV-ID-CREATE-RESET.

  @FEAT-007 @AC-007-022 @HV-ID-CREATE-STAGE-001
  Scenario: A sealed provisional record precedes any network call
    # FEAT-036 must reconcile this historical mnemonic clause and exact network boundary
    # with the approved global no-mnemonic replacement; this original remains pending.
    Given recovery confirmation and a valid device-password capability
    When Create Identity is invoked
    Then the credential bundle, separate mnemonic record, reviewed profile, exact signed transaction, and digest commit atomically
    And no network call occurs before read-back verification

  @FEAT-007 @AC-007-023 @HV-ID-CREATE-STAGE-002 @HV-ID-SUBMISSION-MIGRATED
  Scenario: A provisional identity is never authenticated and blocks first-run
    Given Alice's creation review has a real encrypted provisional vault
    When Alice checks the provisional review and then leaves it without submitting
    Then the same provisional keys stay locked until independent registration and a fresh online unlock

  @FEAT-007 @AC-007-027 @HV-ID-CREATE-STAGE-003 @HV-ID-SUBMISSION-MIGRATED
  Scenario: The exact signed transaction survives process death
    Given Alice's restartable browser has sealed and submitted an identity that is not yet indexed
    When the owned browser process is killed and restarted using its existing encrypted storage
    Then the exact signed bytes survive and ordinary unlock completes only after real identity and licence indexing

  @FEAT-007 @AC-007-052 @HV-ID-CREATE-STAGE-004 @HV-STAGED-RESUME
  Scenario: Restart with a provisional record resumes safely without mnemonic reveal
    Given Alice has genuinely staged creation in a browser that will lose its process
    When the browser restarts with only its persistent staged vault and no onboarding authority
    Then the required staged-resume heading appears before password unlock and exact online activation

  @FEAT-007 @AC-007-054 @HV-ID-CREATE-STAGE-005 @HV-ID-SUBMISSION-MIGRATED
  Scenario: Promotion failure retries only the local transition
    Given Alice has a real accepted identity and the next local lifecycle commit will abort
    When the real worker journal commit aborts and Alice retries only the local save
    Then the preserved identity becomes active without resubmission and real licence indexing opens the workspace

  @FEAT-007 @AC-007-025 @HV-ID-CREATE-SUBMIT-001 @HV-ID-SUBMISSION-MIGRATED
  Scenario: The transaction is canonical
    Given reviewed profile fields and exact candidate addresses
    When the signed transaction is constructed
    Then it uses CSPRNG UUIDv4, a corpus-exact UTC timestamp, approved property order, exact UTF-8 PayloadSize, the exact payload GUID, and the established signed JSON representation

  @FEAT-007 @AC-007-026 @HV-ID-CREATE-SUBMIT-002 @HV-ID-SUBMISSION-MIGRATED
  Scenario: Signatory, payload signing address, and signing key bind exactly
    Given the authority owns the signing key
    When the transaction is signed
    Then UserSignature.Signatory equals the payload signing address
    And both equal the authority-owned signing key

  @FEAT-007 @AC-007-028 @HV-ID-CREATE-SUBMIT-003 @HV-ID-SUBMISSION-MIGRATED
  Scenario: Every cycle performs GetIdentity before submission
    Given Alice has approved creation review but has not queried or submitted the new identity
    When Alice creates the identity and later restarts before its first authenticated unlock
    Then initial admission and restart both query the exact public address before any subsequent transaction

  @FEAT-007 @AC-007-029 @HV-ID-CREATE-SUBMIT-004 @HV-ID-SUBMISSION-MIGRATED
  Scenario: Only authoritative absence permits submission
    Given Alice has approved creation review but has not queried or submitted the new identity
    When Alice requests creation while the real initial identity lookup is unavailable
    Then no transaction is submitted until explicit Retry obtains authoritative absence from the node

  @FEAT-007 @AC-007-030 @HV-ID-CREATE-SUBMIT-005 @HV-ID-SUBMISSION-MIGRATED
  Scenario: Exact keys confirm the same identity; encryption mismatch fails closed
    Given Alice has approved creation review but has not queried or submitted the new identity
    When a real indexed profile has Alice's signing address but a different encryption address
    Then creation fails closed without submitting or activating that mismatched identity
    And a fresh candidate with both keys already indexed completes without recreating its identity

  @FEAT-007 @AC-007-032 @HV-ID-CREATE-SUBMIT-006 @HV-ID-WAITING-LIFECYCLE @HV-ID-SUBMISSION-MIGRATED
  Scenario: ACCEPTED promotes to saved-waiting without claiming confirmation
    Given submission returns ACCEPTED
    When the lifecycle is promoted
    Then the provisional lifecycle becomes saved-user waiting
    And no block confirmation is claimed

  @FEAT-007 @AC-007-033 @HV-ID-CREATE-SUBMIT-007 @HV-ID-WAITING-LIFECYCLE @HV-ID-SUBMISSION-MIGRATED
  Scenario: PENDING waits without another submission
    Given Alice's reviewed identity is ready for controlled duplicate delivery of its exact transaction
    When Alice submits through the browser and the node responds PENDING for the same signing key
    Then HushVoting retains a saved waiting identity after the genuine PENDING reply
    And polling preserves those exact bytes without another submission until real block confirmation

  @FEAT-007 @AC-007-034 @HV-ID-CREATE-SUBMIT-008 @HV-ID-EXACT-CONFIRMATION @HV-ID-SUBMISSION-MIGRATED
  Scenario: ALREADY_EXISTS resolves by exact lookup only
    Given Alice has reviewed a Private identity and its first submission can be delayed before admission
    When another device indexes the same keys with a different Public profile before Alice's delayed submission completes
    Then fresh exact indexed lookup is required after the real ALREADY_EXISTS response
    And authenticated access follows exact identity verification and separate licence indexing without another identity submission

  @FEAT-007 @AC-007-035 @HV-ID-CREATE-SUBMIT-009 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: Status/code combinations use a closed allowlist
    Given Alice can submit a reviewed identity to the real node with a negative reply observer
    When real submission replies are made contradictory unknown or unspecified
    Then invalid replies cannot admit or retry while an ordinary reply completes indexed identity and licence verification

  @FEAT-007 @AC-007-036 @HV-ID-CREATE-SUBMIT-010 @HV-ID-LOST-RESPONSE @HV-ID-CONFIRMATION-MIGRATED
  Scenario: Transport ambiguity preserves the exact transaction
    Given Alice's identity is accepted by the real node but its submission response is lost
    When Alice retries the ambiguous connection before the identity is indexed
    Then the encrypted pending identity retains the exact signed transaction and digest without replacement
    And real identity and licence indexing complete access without another identity transaction

  @FEAT-007 @AC-007-045 @HV-ID-CREATE-SUBMIT-011 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: Ambiguous retries reuse exact signed bytes
    Given Alice loses a real accepted response while a public sealed submission command is observed
    When her actual Web worker retries the sealed transaction after lookup confirms absence
    Then the real node returns PENDING for identical bytes and normal confirmation indexes one identity and its licence

  @FEAT-007 @AC-007-048 @HV-ID-CREATE-SUBMIT-012
  Scenario: A missing transaction can be rebuilt only after verified eligibility
    Given the retained transaction record is missing
    When rebuild eligibility is evaluated
    Then authenticated credential/profile verification AND authoritative absence are both required
    And corruption never counts as missing

  @FEAT-007 @AC-007-050 @HV-ID-CREATE-SUBMIT-013 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: The client never polls as retry or periodically replaces transactions
    Given the waiting gate is active
    When time passes
    Then no submission occurs every three seconds
    And no periodic replacement or liveness submission is implemented

  @FEAT-007 @AC-007-037 @HV-ID-CREATE-CONFIRM-001 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: The waiting gate is accessible and explicit
    Given the transaction is accepted or pending
    When the confirmation gate renders
    Then it states that mempool admission is not block confirmation
    And it offers safe Lock/close guidance without an endless unexplained spinner

  @FEAT-007 @AC-007-038 @HV-ID-CREATE-CONFIRM-002 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: GetIdentity polls every three seconds while eligible
    Given the waiting gate is foregrounded, online, visible, and authority-valid
    When polling runs
    Then GetIdentity is called every three seconds through one coalesced loop
    And submission never happens on a poll
    And indexed identity confirmation automatically advances to the root licence gate

  @FEAT-007 @AC-007-039 @HV-ID-CREATE-CONFIRM-003 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: Polling pauses on background, offline, Lock, or revocation
    Given Alice's unindexed browser identity has an active real three-second confirmation loop
    When the waiting page receives a controlled visibility loss and recovery then goes offline and online
    Then resumed confirmation polls issue no worker operations or user-input events
    And Lock revokes the waiting authority and stops its RPCs until fresh unlock after real indexing

  @FEAT-007 @AC-007-040 @HV-ID-CREATE-CONFIRM-004 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: Check again performs lookup only
    Given the waiting gate with a Check again control
    When the user selects Check again
    Then one immediate coalesced lookup runs
    And no new poll loop or submission is created

  @FEAT-007 @AC-007-042 @HV-ID-CREATE-CONFIRM-005 @HV-ID-EXACT-CONFIRMATION @HV-ID-CONFIRMATION-MIGRATED
  Scenario: Exact confirmation synchronizes and clears
    Given Alice has reviewed a Private identity and its first submission can be delayed before admission
    When another device indexes the same keys with a different Public profile before Alice's delayed submission completes
    Then the active encrypted record atomically adopts the indexed alias and visibility and clears its pending transaction
    And authenticated access follows exact identity verification and separate licence indexing without another identity submission

  @FEAT-007 @AC-007-043 @HV-ID-CREATE-CONFIRM-006
  Scenario: A revoked, expired, or restarted authority requires ordinary unlock
    Given confirmation occurred but the authority is revoked, expired, backgrounded, or restarted
    When authentication is evaluated
    Then ordinary unlock is required before entering HushVoting

  @FEAT-007 @AC-007-044 @HV-ID-CREATE-CONFIRM-007 @HV-ID-CONFIRMATION-MIGRATED
  Scenario: Mempool acceptance alone never enters the authenticated shell
    Given only ACCEPTED or PENDING knowledge exists
    When the shell entry is evaluated
    Then HushVoting does not enter the authenticated shell
    And exact GetIdentity confirmation is required

  @FEAT-007 @AC-007-053 @HV-ID-CREATE-CONFIRM-008 @HV-ID-LOST-RESPONSE @HV-ID-CONFIRMATION-MIGRATED
  Scenario: A lost acceptance response converges without another identity
    Given Alice's identity is accepted by the real node but its submission response is lost
    When the accepted identity indexes before Alice retries the lost response
    Then fresh exact lookup and separate licence indexing restore access with only the original identity transaction

  @FEAT-007 @AC-007-041 @HV-ID-CREATE-DELAY-001 @HV-ID-DELAY-MIGRATED
  Scenario: Three minutes without confirmation enters the delay state
    Given Alice's accepted identity remains unindexed with its exact transaction sealed on the device
    When three real minutes elapse without producing an identity block
    Then the delayed screen stops automatic RPCs and retains the sealed vault while Check again remains lookup-only
    And a manual exact check after real identity indexing completes only through separate licence indexing

  @FEAT-007 @AC-007-046 @HV-ID-CREATE-CORRECT-001
  Scenario: Editable alias rejection reopens Profile only
    Given an allowlisted editable pre-admission code
    When correction runs
    Then only Profile/Review reopens with the same identity
    And a fresh Device-password authorization is required before one new replacement transaction

  @FEAT-007 @AC-007-047 @HV-ID-CREATE-CORRECT-002 @HV-ID-REJECTION-MIGRATED
  Scenario: Cryptographic and unknown rejections fail closed without retry
    Given Alice can submit a reviewed identity to the real node with a negative reply observer
    When real node signature rejections carry every terminal code and an unknown code
    Then terminal rejection codes cannot retry or leak diagnostics and a fresh explicit identity completes indexing

  @FEAT-007 @AC-007-049 @HV-ID-CREATE-RESET-001
  Scenario: Blockchain reset re-registers the same identity
    Given a previously confirmed local identity is authoritatively absent
    When reconciliation completes credential/profile verification
    Then one fresh transaction is created from the same vault identity and latest verified encrypted profile
    And no new recovery words are generated because the chain was reset
