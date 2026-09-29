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

  @HV-TWIN-ENT-ENFORCEMENT-011 @AC-018-005 @AC-018-006
  Scenario: Signed Open just before the annual expiry uses canonical execution time
    Given an encrypted Open-ready election with an annual owner licence
    When its signed Open executes -1 seconds from licence expiry
    Then its persisted Open outcome matches the upper-exclusive expiry boundary

  @HV-TWIN-ENT-ENFORCEMENT-012 @AC-018-005 @AC-018-006
  Scenario: Signed Open exactly at the annual expiry uses canonical execution time
    Given an encrypted Open-ready election with an annual owner licence
    When its signed Open executes 0 seconds from licence expiry
    Then its persisted Open outcome matches the upper-exclusive expiry boundary

  @HV-TWIN-ENT-ENFORCEMENT-013 @AC-018-005 @AC-018-006
  Scenario: Signed Open after the annual expiry uses canonical execution time
    Given an encrypted Open-ready election with an annual owner licence
    When its signed Open executes 1 seconds from licence expiry
    Then its persisted Open outcome matches the upper-exclusive expiry boundary

  @HV-TWIN-ENT-ENFORCEMENT-014 @AC-018-005 @AC-018-006
  Scenario: Only an upgrade before Open in the same block affects its capture
    Given an encrypted Open-ready election with an annual owner licence
    When a cap-raising licence executes before Open in the same block
    Then only the earlier licence can authorize that Open

  @HV-TWIN-ENT-ENFORCEMENT-015 @AC-018-005 @AC-018-006
  Scenario: Only an upgrade after Open in the same block affects its capture
    Given an encrypted Open-ready election with an annual owner licence
    When a cap-raising licence executes after Open in the same block
    Then only the earlier licence can authorize that Open

  @HV-TWIN-ENT-ENFORCEMENT-016 @AC-018-006 @AC-018-009
  Scenario: PostgreSQL capture failure rolls back actual Open and suppresses completion
    Given an encrypted Open-ready election with an annual owner licence
    When PostgreSQL refuses its Open capture and that committed block is retried
    Then the fault leaves no partial Open and retry persists one consistent capture

  @HV-TWIN-ENT-ENFORCEMENT-017 @AC-018-006
  Scenario: Committed Open authorization survives an actual node-process crash
    Given an encrypted Open-ready election with an annual owner licence
    When the recorded Open survives an owned node-process crash and restart
    Then the new node process reads the identical durable Open capture and frozen roster

  @HV-TWIN-ENT-ENFORCEMENT-018 @AC-018-005 @AC-018-006
  Scenario: Governed Open executes -1 seconds from owner licence expiry
    Given an encrypted governed Open with two earlier trustee approvals
    When the final signed trustee approval executes -1 seconds from owner expiry
    Then governed Open uses execution-time owner rights and preserves its proposal reference

  @HV-TWIN-ENT-ENFORCEMENT-019 @AC-018-005 @AC-018-006
  Scenario: Governed Open executes 0 seconds from owner licence expiry
    Given an encrypted governed Open with two earlier trustee approvals
    When the final signed trustee approval executes 0 seconds from owner expiry
    Then governed Open uses execution-time owner rights and preserves its proposal reference

  @HV-TWIN-ENT-ENFORCEMENT-020 @AC-018-005 @AC-018-006
  Scenario: Governed Open executes 1 seconds from owner licence expiry
    Given an encrypted governed Open with two earlier trustee approvals
    When the final signed trustee approval executes 1 seconds from owner expiry
    Then governed Open uses execution-time owner rights and preserves its proposal reference
