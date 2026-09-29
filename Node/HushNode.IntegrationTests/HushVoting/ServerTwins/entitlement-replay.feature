@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ENTITLEMENT-REPLAY-TWIN @FEAT-018
Feature: Historical licence authority through the real indexing dispatcher
  EPIC-002 -> FEAT-018 AC-001/009/010/011 -> P018-3-01 -> T018-3-01.
  The real node produces signed historical blocks; browser and query clocks stay unchanged.

  @HV-TWIN-ENT-REPLAY-001 @AC-018-011
  Scenario: Duplicate historical blocks converge after the annual assignment has expired
    Given real historical licence blocks whose annual assignment has now expired
    When their real indexing dispatcher replays the retained blocks
    Then the retained licence assignments and revision are unchanged

  @HV-TWIN-ENT-REPLAY-002 @AC-018-011
  Scenario: Rebuilding the licence projection retains original block-time terms
    Given real historical licence blocks whose annual assignment has now expired
    When the licence projection is rebuilt by replaying the retained blocks
    Then rebuilt licence terms and chain references match the original projection

  @HV-TWIN-ENT-REPLAY-003 @AC-018-009 @AC-018-010
  Scenario: Missing retained catalogue prevents a successful indexing checkpoint
    Given real historical licence blocks whose annual assignment has now expired
    When a dispatcher cannot resolve the retained approved licence release
    Then indexing fails without advancing its completion callback or changing rights

  @HV-TWIN-ENT-REPLAY-004 @AC-018-001 @AC-018-011
  Scenario: Current authority queries filter historical assignments in PostgreSQL
    Given real historical licence blocks whose annual assignment has now expired
    When the indexed authority reader queries the assignment at its effective instant
    Then PostgreSQL filters the current row and the original assigned terms are returned
