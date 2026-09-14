@HushVoting @HV-E2E @E2E @HV-RECOVERY-WORDS @HV-FEAT-008
@FEAT-008
Feature: Recovery words — entry-input-validation
  Covers HV-RW-ENTRY-GUARD, HV-RW-INPUT, HV-RW-PASTE, HV-RW-VALIDATE.

  @FEAT-008 @AC-008-001 @HV-RW-ENTRY-GUARD-001 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-001 — HV-RW-ENTRY-GUARD
    Given Alice has real staged and active vault snapshots and her original registered recovery words
    When stale recovery entries encounter staged active rollback removal corrupt and competing custody
    Then every fresh recovery inspection blocks phrase entry without deriving or replacing credentials
    And verified empty custody permits the original recovery words to restore through the live node

  @FEAT-008 @AC-008-002 @HV-RW-ENTRY-GUARD-002 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-002 — HV-RW-ENTRY-GUARD
    Given an authenticated session is active
    When Alice locks the app before attempting recovery
    Then the local identity remains and recovery choices stay unavailable
    And only confirmed local removal makes Recovery Words available

  @FEAT-008 @AC-008-003 @HV-RW-ENTRY-GUARD-003 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-003 — HV-RW-ENTRY-GUARD
    Given a provisioned vault exists on this device
    When Alice requests local reset without knowing the device password
    Then the destructive warning and explicit confirmation precede deletion
    And verified cleanup completes before the recovery choices appear

  @FEAT-008 @AC-008-004 @HV-RW-ENTRY-GUARD-004
  Scenario: AC-008-004 — HV-RW-ENTRY-GUARD
    Given verified empty local state
    When the entry guard inspects the local authority
    Then recovery starts only with no active, staged, rollback, quarantine, or competing authority

  @FEAT-008 @AC-008-005 @HV-RW-INPUT-001 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-005 — HV-RW-INPUT
    Given a twelve-or-twenty-four word selector with indexed fields
    When the user selects a word count
    Then exactly that many indexed responsive fields render with accessible labels

  @FEAT-008 @AC-008-006 @HV-RW-VALIDATE-001 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-006 — HV-RW-VALIDATE
    Given a twelve-or-twenty-four word selector with indexed fields
    When Alice corrects an unknown word and verifies Unicode-normalized forms of the same phrase
    Then normalization preserves the independently derived public-key pair without substituting unknown words

  @FEAT-008 @AC-008-007 @HV-RW-VALIDATE-002 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-007 — HV-RW-VALIDATE
    Given a focused word box and a clipboard phrase
    When Alice submits unsupported recovery counts and non-English words
    Then unsupported recovery input stays local and no BIP39 passphrase is collected

  @FEAT-008 @AC-008-008 @HV-RW-PASTE-001 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-008 — HV-RW-PASTE
    Given a focused word box and a clipboard phrase
    When Alice pastes a complete phrase from every supported field position
    Then the whole grid receives exactly one normalized word per field

  @FEAT-008 @AC-008-009 @HV-RW-PASTE-002 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-009 — HV-RW-PASTE
    Given a focused word box and a clipboard phrase
    When Alice pastes mismatched and unsupported phrase counts over existing words
    Then the paste reports a count error and leaves every existing field and the selected count unchanged

  @FEAT-008 @AC-008-010 @HV-RW-PASTE-003 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-010 — HV-RW-PASTE
    Given a focused word box and a clipboard phrase
    When Alice pastes a replacement phrase over existing words
    Then only explicit Replace all changes the entire grid and Cancel retains the original words

  @FEAT-008 @AC-008-011 @HV-RW-PASTE-004 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-011 — HV-RW-PASTE
    Given a focused word box and a clipboard phrase
    When Alice pastes a count-correct phrase containing unknown words
    Then only unknown numbered positions are marked and their values are never echoed

  @FEAT-008 @AC-008-012 @HV-RW-PASTE-005 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-012 — HV-RW-PASTE
    Given a focused word box and a clipboard phrase
    When Alice pastes from her clipboard and clears the recovery grid
    Then the clipboard retains the source phrase and no file is read by the app

  @FEAT-008 @AC-008-013 @HV-RW-INPUT-002 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-013 — HV-RW-INPUT
    Given a twelve-or-twenty-four word selector with indexed fields
    When Alice enters recovery words and moves focus between fields
    Then only the focused word is visible until an explicit Show action
    And Hide and lifecycle loss conceal all completed words

  @FEAT-008 @AC-008-014 @HV-RW-INPUT-003 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-014 — HV-RW-INPUT
    Given a twelve-or-twenty-four word selector with indexed fields
    When Alice submits unknown words or a phrase with an invalid checksum
    Then numbered validation errors retain concealed inputs for correction
    And the invalid phrase is neither persisted nor sent to HushServerNode

  @FEAT-008 @AC-008-015 @HV-RW-VALIDATE-003 @HV-RECOVERY-INPUT-MIGRATED
  Scenario: AC-008-015 — HV-RW-VALIDATE
    Given a twelve-or-twenty-four word selector with indexed fields
    When Alice repeatedly corrects invalid phrases then double-clicks Verify for valid words
    Then validation remains recoverable and starts only one bounded candidate lookup
