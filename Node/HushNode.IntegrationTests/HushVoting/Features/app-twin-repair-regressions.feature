@HushVoting @HV-E2E @E2E @HV-APP-TWIN-REPAIR
Feature: Web integration counterparts of the App Twin repairs
  EPIC-001; FEAT-007 Phase 2 Tasks 2.1/2.2 and Phase 7 Tasks 7.1/7.2;
  FEAT-008 Phase 3 Tasks 3.5/3.6; FEAT-009 Phase 3 Tasks 3.7/3.8;
  both recovery features Phase 6 Tasks 6.1/6.2 and Phase 7 Tasks 7.1/7.2.
  Supplemental assertions only: retained frontend mappings remain owned by TypeScript.

  @FEAT-007 @AC-007-016 @HV-REPAIR-CREATION-PROPS
  Scenario: Real creation reveal keeps the full phrase outside React props
    Given the creation flow runs
    Then the real creation display excludes recovery words from React props

  @FEAT-008 @AC-008-051 @HV-REPAIR-WORDS-SESSION
  Scenario: Real recovery words session has no saved identity after process loss
    Given Alice restores her registered identity from words to choose a temporary Web session
    When Alice explicitly chooses and acknowledges session-only protection without a device password
    Then browser process loss leaves no remembered session identity and requires recovery input again

  @FEAT-008 @AC-008-051 @HV-REPAIR-WORDS-LOCK
  Scenario: Explicit Lock forgets the temporary recovery words identity
    Given Alice restores her registered identity from words to choose a temporary Web session
    When Alice explicitly chooses and acknowledges session-only protection without a device password
    Then explicit Lock forgets the temporary identity and returns to first-run choices

  @FEAT-009 @AC-009-054 @HV-REPAIR-FILE-LOCK
  Scenario: Explicit Lock forgets the temporary credential file identity
    Given Alice restores her registered identity from a credential file to choose a temporary Web session
    When Alice explicitly chooses and acknowledges session-only protection without a device password
    Then explicit Lock forgets the temporary identity and returns to first-run choices

  @FEAT-009 @AC-009-041 @HV-REPAIR-HISTORICAL-BOUNDS
  Scenario: Real node historical metadata is preserved and oversized metadata fails closed
    Given an indexed credential identity has historical markup and unsafe controls in its authoritative alias
    When credential restoration reaches the real entitlement gate and workspace with the historical profile
    Then only displayed controls are replaced while a gross oversized profile cannot enable another import

  @FEAT-009 @AC-009-054 @HV-REPAIR-FILE-SESSION
  Scenario: Real credential file session has no saved identity after process loss
    Given Alice restores her registered identity from a credential file to choose a temporary Web session
    When Alice explicitly chooses and acknowledges session-only protection without a device password
    Then browser process loss leaves no remembered session identity and requires recovery input again
