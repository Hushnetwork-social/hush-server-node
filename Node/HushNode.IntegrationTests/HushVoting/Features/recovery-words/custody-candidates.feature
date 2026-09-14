@HushVoting @HV-E2E @E2E @HV-RECOVERY-WORDS @HV-FEAT-008
@FEAT-008
Feature: Recovery words — custody-candidates
  Covers HV-RW-CUSTODY, HV-RW-CANDIDATES, HV-RW-CONTROL.

  @FEAT-008 @AC-008-016 @HV-RW-CUSTODY-001 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-016 — HV-RW-CUSTODY
    Given Alice enters controlled recovery words with a bounded handoff observer
    When Alice verifies once and the real worker resolves the recovered candidates
    Then one normalized phrase reaches the worker and cleared page inputs cannot return through history

  @FEAT-008 @AC-008-017 @HV-RW-CUSTODY-002
  Scenario: AC-008-017 — HV-RW-CUSTODY
    Given Alice enters recovery words with observed React worker network and storage boundaries
    When Alice confirms replacement verifies and completes real recovered registration and protection
    Then the recovery journey contains no secret in ordinary UI state transport history storage or completed artifacts

  @FEAT-008 @AC-008-018 @HV-RW-CANDIDATES-001 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-018 — HV-RW-CANDIDATES
    Given a checksum-valid phrase
    When Alice verifies supported phrases with twelve and twenty-four words
    Then only applicable approved formats reach the node and their addresses match independent derivation

  @FEAT-008 @AC-008-019 @HV-RW-CANDIDATES-002 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-019 — HV-RW-CANDIDATES
    Given a checksum-valid phrase
    When Alice verifies the twenty-four-word phrase
    Then exact historical address-pair duplicates preserve their source labels while distinct pairs remain separate

  @FEAT-008 @AC-008-020 @HV-RW-CANDIDATES-003 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-020 — HV-RW-CANDIDATES
    Given Alice enters valid recovery words before a controlled worker producer failure
    When the second applicable producer encounters an actual encoding exception
    Then no partial candidate reaches lookup and fresh recovery can register the original keys

  @FEAT-008 @AC-008-021 @HV-RW-CANDIDATES-004 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-021 — HV-RW-CANDIDATES
    Given Alice resolves both approved recovery candidates with a disposal observer
    When Alice selects one candidate and the worker acknowledges disposal of the other
    Then only the selected recovered keys enter the vault and complete real registration

  @FEAT-008 @AC-008-022 @HV-RW-CONTROL-001
  Scenario: AC-008-022 — HV-RW-CONTROL
    Given a selected candidate
    When the selected-key control proof runs locally
    Then exact signing and encryption consistency is proven before staging

  @FEAT-008 @AC-008-023 @HV-RW-CUSTODY-003
  Scenario: AC-008-023 — HV-RW-CUSTODY
    Given a valid phrase in the input component
    When Verify transfers the phrase to the secret authority
    Then page buffers clear and the phrase never enters state, storage, logs, or history
