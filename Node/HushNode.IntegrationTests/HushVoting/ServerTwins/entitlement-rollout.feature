@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ENTITLEMENT-ROLLOUT-TWIN @FEAT-018
Feature: Coordinated entitlement rollout preserves original authority
  EPIC-002 -> FEAT-018 AC-009/010/011 -> P018-6-01/02 -> T018-6-01/02.
  Ordinary node composition, retained signed blocks and real isolated PostgreSQL.
  Deliberate projection faults below affect only the scenario-owned database.

  Background:
    Given the rollout block clock retains submicrosecond UTC precision

  @HV-TWIN-ENT-ROLLOUT-001
  Scenario: Recover an exactly provable capture after current licence expiry
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When only its rebuildable capture projection is lost
    Then readiness reconstructs the identical original capture without changing current rights

  @HV-TWIN-ENT-ROLLOUT-002
  Scenario: Preserve and refuse incompatible capture semantics
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When rollout encounters incompatible capture
    Then rollout refuses with election_capture_incompatible and preserves every existing capture

  @HV-TWIN-ENT-ROLLOUT-003
  Scenario: A persisted chain head cannot substitute for completed indexing
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When rollout encounters missing checkpoint
    Then rollout refuses with election_index_checkpoint_incomplete and preserves every existing capture

  @HV-TWIN-ENT-ROLLOUT-004
  Scenario: Missing retained release cannot borrow the current catalogue
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When rollout encounters missing retained release
    Then rollout refuses with election_licence_history_unprovable and preserves every existing capture

  @HV-TWIN-ENT-ROLLOUT-005
  Scenario: Pinned projection terms must match the original signed assignment
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When rollout encounters unprovable assignment
    Then rollout refuses with election_licence_history_unprovable and preserves every existing capture

  @HV-TWIN-ENT-ROLLOUT-006
  Scenario: Redis loss does not change authoritative rollout readiness
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When the run-owned Redis is unavailable during rollout verification
    Then the retained PostgreSQL authority remains ready and unchanged

  @HV-TWIN-ENT-ROLLOUT-007
  Scenario: Incompatible schema rollback cannot discard successful completion evidence
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When an operator attempts to remove the populated completion-checkpoint schema
    Then the retained PostgreSQL authority remains ready and unchanged

  @HV-TWIN-ENT-ROLLOUT-008
  Scenario: Unsupported signed client semantics cannot open an election
    Given an encrypted Open-ready election with an annual owner licence
    When a correctly signed client uses an unsupported election envelope version

  @HV-TWIN-ENT-ROLLOUT-009
  Scenario: Production database guards reject evidence edits
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When ordinary database writes attempt to change immutable authorization evidence
    Then the retained PostgreSQL authority remains ready and unchanged

  @HV-TWIN-ENT-ROLLOUT-010
  Scenario: Governed capture recovery keeps the actual execution proposal
    Given a genuinely indexed governed Open whose licence is now expired
    When only its rebuildable capture projection is lost
    Then readiness reconstructs the identical original capture without changing current rights

  @HV-TWIN-ENT-ROLLOUT-011
  Scenario: Additive upgrade preserves prior-schema evidence without inventing indexing success
    Given an encrypted Open-ready election with an annual owner licence
    And a genuinely indexed Open whose licence is now expired
    When the additive checkpoint migration upgrades a populated prior-schema snapshot
    Then rollout refuses with election_index_checkpoint_incomplete and preserves every existing capture
