@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ENTITLEMENT-ENFORCEMENT-TWIN @FEAT-018
Feature: Election entitlement lifecycle enforcement
  EPIC-002 -> FEAT-018 AC-001/002/003/004 -> P018-3-02 -> T018-3-02.
  Real node-issued licences and real PostgreSQL lifecycle transactions.
  These Draft scenarios enter at the application-service boundary with an explicit trusted
  dispatch-frame fixture; signed direct/encrypted Open dispatch has separate scenarios.

  @HV-TWIN-ENT-ENFORCEMENT-001 @AC-018-002
  Scenario: hushvoting.direct.free accepts 100 eligible voters and rejects one more
    Given an owned Draft with the indexed hushvoting.direct.free licence
    When its canonical roster reaches 100 voters and one more is requested
    Then the cap-sized roster remains unchanged after the typed rejection

  @HV-TWIN-ENT-ENFORCEMENT-002 @AC-018-002
  Scenario: hushvoting.veritas.500 accepts 500 eligible voters and rejects one more
    Given an owned Draft with the indexed hushvoting.veritas.500 licence
    When its canonical roster reaches 500 voters and one more is requested
    Then the cap-sized roster remains unchanged after the typed rejection

  @HV-TWIN-ENT-ENFORCEMENT-003 @AC-018-002
  Scenario: hushvoting.veritas.2000 accepts 2000 eligible voters and rejects one more
    Given an owned Draft with the indexed hushvoting.veritas.2000 licence
    When its canonical roster reaches 2000 voters and one more is requested
    Then the cap-sized roster remains unchanged after the typed rejection

  @HV-TWIN-ENT-ENFORCEMENT-004 @AC-018-002
  Scenario: hushvoting.veritas.10000 accepts 10000 eligible voters and rejects one more
    Given an owned Draft with the indexed hushvoting.veritas.10000 licence
    When its canonical roster reaches 10000 voters and one more is requested
    Then the cap-sized roster remains unchanged after the typed rejection

  @HV-TWIN-ENT-ENFORCEMENT-005 @AC-018-003
  Scenario: Full replacement works before linking and stays blocked after a link
    Given an owned Draft with the indexed hushvoting.direct.free licence
    When its unlinked roster is replaced and a voter then links
    Then replacement after a fresh context preserves that roster and its first-link evidence

  @HV-TWIN-ENT-ENFORCEMENT-006 @AC-018-002
  Scenario: Concurrent additions cannot jointly exceed the cap
    Given an owned Draft with the indexed hushvoting.direct.free licence
    When two additions compete for the final roster slot
    Then one addition commits and the other has the typed cap rejection

  @HV-TWIN-ENT-ENFORCEMENT-007 @AC-018-003
  Scenario: Identity linking and full replacement serialize
    Given an owned Draft with the indexed hushvoting.direct.free licence
    When identity linking races with replacement by a different roster
    Then only a consistent linked-old or unlinked-new roster survives

  @HV-TWIN-ENT-ENFORCEMENT-008 @AC-018-002
  Scenario: Canonical duplicate rejection survives entitlement checks
    Given an owned Draft with the indexed hushvoting.direct.free licence
    When an import duplicates an existing canonical voter identifier
    Then duplicate rejection retains the original roster and records rejection evidence

  @HV-TWIN-ENT-ENFORCEMENT-009 @AC-018-001 @AC-018-004
  Scenario: A licensed actor cannot edit another owner's draft or choose an unlicensed profile
    Given an owned Draft with the indexed hushvoting.direct.free licence
    When a different actor attempts import and the owner chooses trustee governance
    Then actor and profile rejections preserve the draft

  @HV-TWIN-ENT-ENFORCEMENT-010 @AC-018-004
  Scenario: An invitation locks the selected governance despite a sufficient licence
    Given an owned Draft with the indexed hushvoting.veritas.2000 licence
    When the owner selects trustee governance and issues its first invitation
    Then switching to another licensed trustee profile is rejected without deleting evidence
