@HushVoting @HV-E2E @E2E @HV-LIC-COMPATIBILITY @HV-FEAT-016 @FEAT-016
Feature: Rejection of incompatible entitlement transport responses
  Supplemental EPIC-002 AT-LIC-012 negative matrix, FEAT-016 AC-016-018.
  A real indexed node response is corrupted at its server transport boundary.
  This qualifies rejection and healthy recovery, not a future server release
  or the separate recognized historical retired/disabled catalogue examples.

  @EPIC-002 @AT-LIC-012 @AC-016-018 @HV-LIC-COMPAT-PLAN
  Scenario: Unknown plan in a real entitlement reply cannot authorize the workspace
    Given the real active entitlement reply is corrupted with an unknown plan
    When HushVoting validates the response
    Then workspace remains gated with compatible-client guidance
    And it is not mapped to Direct Free or known Veritas
    And no baseline transaction is created
    And an uncorrupted fresh node response restores the same indexed entitlement

  @EPIC-002 @AT-LIC-012 @AC-016-018 @HV-LIC-COMPAT-FAMILY
  Scenario: Unknown family in a real entitlement reply cannot authorize the workspace
    Given the real active entitlement reply is corrupted with an unknown family
    When HushVoting validates the response
    Then workspace remains gated with compatible-client guidance
    And it is not mapped to Direct Free or known Veritas
    And no baseline transaction is created
    And an uncorrupted fresh node response restores the same indexed entitlement

  @EPIC-002 @AT-LIC-012 @AC-016-018 @HV-LIC-COMPAT-GOVERNANCE
  Scenario: Unknown governance in a real entitlement reply cannot authorize the workspace
    Given the real active entitlement reply is corrupted with unknown governance
    When HushVoting validates the response
    Then workspace remains gated with compatible-client guidance
    And it is not mapped to Direct Free or known Veritas
    And no baseline transaction is created
    And an uncorrupted fresh node response restores the same indexed entitlement
