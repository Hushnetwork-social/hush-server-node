@HushVoting @HV-IDENTITY-CREATE @HV-FEAT-007
@FEAT-007
Feature: Identity create — navigation, multi-owner, cancellation, security, native
  Covers HV-ID-CREATE-NAV, HV-ID-CREATE-MULTI, HV-ID-CREATE-CANCEL,
  HV-ID-CREATE-SECURITY, HV-ID-CREATE-NATIVE.

  @FEAT-007 @AC-007-058 @HV-ID-CREATE-NAV-001 @HV-E2E @E2E
  Scenario: Browser, Android, and in-app Back share one typed authority
    # Web policy selected by the user in FEAT-027: discard and exit to root.
    # Original title/AC retained; Android qualification remains separate and unclaimed.
    Given Alice checks both Web Back controls at every unsaved creation screen
    When Alice starts creation again after discarding the final candidate
    Then fresh generation was required and no abandoned creation reached the real node

  @FEAT-007 @AC-007-059 @HV-ID-CREATE-NAV-002 @HV-ID-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: After provisional persistence, stale history cannot reopen creation
    Given Alice's creation review has a real encrypted provisional vault
    When Alice navigates Back then Forward and reloads the provisional creation history
    Then the same provisional keys stay locked until independent registration and a fresh online unlock

  @FEAT-007 @AC-007-060 @HV-ID-CREATE-NAV-003 @HV-ID-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: Forged, stale, or restored onboarding tokens are rejected
    Given Alice retries rejected creation cleanup before starting a fresh provisional review
    When Alice presents stale and forged creation tokens and manually navigates to a creation query
    Then the same provisional keys stay locked until independent registration and a fresh online unlock

  @FEAT-007 @AC-007-061 @HV-ID-CREATE-MULTI-002 @HV-E2E @E2E
  Scenario: A non-secret cross-tab event invalidates stale onboarding authorities
    Given another tab commits a local user
    When the non-secret local-user event is observed or vault inspection runs
    Then the stale onboarding authority is invalidated
    And no alias, address, transaction, password, mnemonic, or key is broadcast

  @FEAT-007 @AC-007-062 @HV-ID-CREATE-MULTI-003 @HV-E2E @E2E
  Scenario: Single-owner authority prevents competing candidates
    Given two tabs/windows/processes attempt provisioning
    When ownership is evaluated
    Then only the single owner may provision or submit
    And a second owner cannot create a competing FEAT-007 candidate

  @FEAT-007 @AC-007-031 @HV-ID-CREATE-MULTI-001 @HV-E2E @E2E
  Scenario: Concurrent triggers coalesce into one reconciliation cycle
    Given startup, focus, connectivity, and user retry occur together
    When reconciliation starts
    Then one authority-owned lookup runs
    And at most one submission follows authoritative absence

  @FEAT-007 @AC-007-055 @HV-ID-CREATE-CANCEL-001 @HV-E2E @E2E
  Scenario: Pre-submit cancellation verifies rollback before restoring first-run
    Given no submission has been attempted
    When cancellation is confirmed
    Then rollback is invoked with destructive confirmation and fresh Device-password authorization
    And first-run is restored only after storage absence is verified

  @FEAT-007 @AC-007-056 @HV-ID-CREATE-CANCEL-002 @HV-E2E @E2E
  Scenario: Post-submit cancellation warns that blockchain creation cannot be cancelled
    Given a submission may have occurred
    When local cancellation is confirmed
    Then the warning states the transaction may still confirm and saved recovery words are required to restore
    And fresh authorization and acknowledgement are required

  @FEAT-007 @AC-007-057 @HV-ID-CREATE-CANCEL-003 @HV-E2E @E2E
  Scenario: Rollback failure quarantines the authority
    Given rollback cannot verify storage absence
    When cleanup is evaluated
    Then the authority is quarantined, capabilities revoked, and first-run/authentication blocked
    And tombstone-backed cleanup retries until every slot is verified absent

  @FEAT-007 @AC-007-016 @HV-ID-CREATE-SECURITY-001 @HV-E2E @E2E
  Scenario: Secrets never enter React, XState, history, logs, or plaintext storage
    Given the creation flow runs
    When secrets are handled
    Then private keys and the full phrase never enter React, XState, route/history state, logs, analytics, or persistent plaintext storage

  @FEAT-007 @AC-007-024 @HV-ID-CREATE-SECURITY-002 @HV-ID-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: Each platform uses only its sealed operation seams
    # Web realization of AC-007-024; Ubuntu/Android qualification remains separate.
    Given Alice protects a Web identity while only public worker operation metadata is observed
    When generic signing decryption and private-key export requests reach her real Web worker
    Then the requests produce no result or storage mutation and sealed creation completes real identity and licence indexing

  @FEAT-007 @AC-007-067 @HV-ID-CREATE-SECURITY-003 @HV-ID-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: No social or feed state is initialized during onboarding
    Given an active reveal authority
    When Alice protects and registers that generated identity
    Then identity onboarding creates no personal feed or social state before the separate licence gate

  @FEAT-007 @AC-007-069 @HV-ID-CREATE-SECURITY-004 @HV-ARTIFACT-PRIVACY @HV-ID-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: Secret-bearing scenarios disable capture and artifact scanning finds nothing
    Given a scenario that displays recovery words or accepts a password
    When evidence is collected
    Then trace, screenshot, and video capture are disabled before exposure
    And artifact scanning finds no mnemonic-like sequences, private keys, passwords, or full transactions

  @FEAT-007 @AC-007-071 @HV-ID-CREATE-SECURITY-005 @HV-SERVER-TWIN @NON_E2E @HV-ORIGINAL-IDENTITY-TWIN
  Scenario: Server TwinTests prove rejection and confirmation semantics
    Given a real node process has indexed and cached a P01 private identity
    When that node is stopped and its owned chain database and Redis are reset before a new node starts
    Then forged recreation is rejected and one fresh same-key transaction restores the exact profile

  @FEAT-007 @AC-007-072 @HV-ID-CREATE-SECURITY-006 @HV-ID-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: Concurrent admission produces exactly one ACCEPTED
    Given Alice's reviewed browser transaction will compete with seven exact retransmissions at the real node
    When all eight identity RPCs arrive before their admission barrier is released
    Then the node returns one ACCEPTED and seven PENDING replies for those exact signed bytes
    And Alice retains the same pending keys until identity and licence indexing unlock the browser shell

  @FEAT-007 @AC-007-073 @HV-ID-CREATE-SECURITY-007 @HV-E2E @E2E
  Scenario: The fault matrix converges safely
    Given fault injection at journal, network, promotion, polling, synchronization, deletion, and ownership boundaries
    When each interruption occurs
    Then every interruption converges to one safe state
    And no duplicate candidate/submission, secret exposure, or false authentication occurs

  @FEAT-007 @AC-007-076 @HV-ID-CREATE-SECURITY-008 @HV-E2E @E2E @HV-BACKEND-RELEASE
  Scenario: The external hardening blocker is green before completion
    Given the complete HushServerNode signature binding admission status and TwinTest release matrix has verified current-build evidence
    When the real Web missing-profile flow submits its exact signed identity and waits for indexed confirmation
    Then the complete backend prerequisite and this indexed Web journey share the executed build without approving other release gates

  @FEAT-007 @AC-007-064 @HV-ID-CREATE-NATIVE-001 @HV-E2E @E2E
  Scenario: Ubuntu uses native custody and never exposes credentials to the WebView
    Given a qualified Secret Service or its approved explicit fallback
    When identity creation runs
    Then credentials, signing, and transport remain native
    And the WebView receives only safe projections

  @FEAT-007 @AC-007-065 @HV-ID-CREATE-NATIVE-002 @HV-E2E @E2E
  Scenario: Android requires hardware-backed protection with no fallback
    Given secure lock or qualified hardware-backed Keystore is absent
    When Create User preflight runs
    Then generation is blocked
    And no browser, software, or password-only fallback is selected

  @FEAT-007 @AC-007-066 @HV-ID-CREATE-NATIVE-003 @HV-E2E @E2E @HV-WEB-TRANSPORT-CONFORMANCE
  Scenario: Adapters observe the same server reply equivalently
    # Web portion only. Original native equivalence remains pending under Phase 6 Task 6.4;
    # a Web pass cannot qualify Ubuntu or Android adapters or close the full criterion.
    Given Alice can compare real node replies with the production Web BFF and worker
    When real admission and indexing produce Accepted Pending AlreadyExists and Rejected for her Web creation requests
    Then the Web mappings preserve node fields and require exact lookup and licence indexing before authenticated access

  @FEAT-007 @AC-007-070 @HV-ID-CREATE-NATIVE-004 @HV-E2E @E2E
  Scenario: Every acceptance criterion maps to executable Gherkin
    Given the FEAT-007 acceptance catalog
    When the coverage manifest validator runs
    Then all 76 criteria have executable scenarios
    And every scenario references known criteria and a declared target

  @FEAT-007 @AC-007-075 @HV-ID-CREATE-NATIVE-005 @HV-E2E @E2E @HV-RELEASE-PINS
  Scenario: Release evidence pins immutable digests
    # Input integrity is executable; full release/handoff qualification remains separately evaluated.
    Given Alice completes the real creation Web journey for input pin evidence
    When the exact running build and public contract inputs are recorded by content digest
    Then the recorded pins match the executed sources builds public corpora dependencies and Web handoff definitions
    And input pin integrity does not promote missing qualifications or assert release readiness
