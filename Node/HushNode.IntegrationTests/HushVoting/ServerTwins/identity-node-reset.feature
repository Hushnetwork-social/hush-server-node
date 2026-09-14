@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-NODE-RESET-TWIN @FEAT-011
Feature: HushVoting same-key registration after an actual owned blockchain reset
  FEAT-011 Phase 3 Tasks 3.3–3.8; backend FEAT evidence only.
  Browser custody, explicit intent and encrypted metadata reconciliation retain FEAT-019 ownership.

  @HV-TWIN-ID-RESET-001 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: A reset chain requires fresh authenticated registration of retained P01 keys
    Given a real node process has indexed and cached a P01 private identity
    When that node is stopped and its owned chain database and Redis are reset before a new node starts
    Then forged recreation is rejected and one fresh same-key transaction restores the exact profile

  @HV-TWIN-ID-RESET-002 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: A reset chain preserves the exact P02 keys and public profile on re-registration
    Given a real node process has indexed and cached a P02 public identity
    When that node is stopped and its owned chain database and Redis are reset before a new node starts
    Then forged recreation is rejected and one fresh same-key transaction restores the exact profile
