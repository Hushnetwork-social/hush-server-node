@HushVoting @HV-SERVER-TWIN @NON_E2E @FEAT-011
Feature: HushVoting identity admission across actual node process death
  FEAT-011 Phase 3 Tasks 3.3/3.4, 3.7/3.8.
  Supporting FEAT-007/008/009 backend evidence, not EPIC browser acceptance.

  @HV-NODE-RESTART-TWIN @HV-TWIN-ID-RESTART-001 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: An unindexed exact transaction is admitted once again after node death
    Given a real node process has accepted an unindexed identity transaction
    When that node process is killed and a new process opens the same database and cache
    Then exact retry follows persisted identity truth and indexes only one unchanged profile

  @HV-NODE-RESTART-TWIN @HV-TWIN-ID-RESTART-002 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: An indexed identity survives node death without another admission
    Given a real node process has accepted an indexed identity transaction
    When that node process is killed and a new process opens the same database and cache
    Then exact retry follows persisted identity truth and indexes only one unchanged profile
