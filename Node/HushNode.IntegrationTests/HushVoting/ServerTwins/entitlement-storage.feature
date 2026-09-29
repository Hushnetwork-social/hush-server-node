@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ENTITLEMENT-STORAGE-TWIN @FEAT-018
Feature: Immutable election entitlement storage
  EPIC-002 -> FEAT-018 AC-003/006/010/011 -> P018-2-01/03 -> T018-2-01/03.
  These scenarios exercise storage with explicit contract fixtures, not lifecycle authorization.
  Phase 3 owns actual signed Open and claim/roster concurrency acceptance.

  @HV-TWIN-ENT-STORAGE-001 @AC-018-006
  Scenario: Interrupted capture and election state write roll back together
    Given an owned PostgreSQL election with supported capture contract facts
    When an election and capture transaction is interrupted before commit
    Then neither Open state nor capture persists and a committed retry persists both

  @HV-TWIN-ENT-STORAGE-002 @AC-018-006 @AC-018-011
  Scenario: Capture replay converges but changed evidence cannot overwrite it
    Given an owned PostgreSQL election with supported capture contract facts
    When the same capture is replayed and different terms are attempted
    Then exactly one unchanged capture survives both application and direct SQL overwrite attempts

  @HV-TWIN-ENT-STORAGE-003 @AC-018-003
  Scenario: First link evidence survives a fresh context and cannot be cleared
    Given an owned PostgreSQL election with supported capture contract facts
    When first link evidence is committed and the context is reopened
    Then the original link boundary remains and direct deletion is rejected

  @HV-TWIN-ENT-STORAGE-004 @AC-018-009 @AC-018-010
  Scenario: Unsupported capture semantics cannot create authorization evidence
    Given an owned PostgreSQL election with supported capture contract facts
    When a capture with an unsupported schema is submitted
    Then no capture is persisted and the election stays Draft

  @HV-TWIN-ENT-STORAGE-005 @AC-018-010
  Scenario: Downgrade refuses to discard captured completion rights
    Given an owned PostgreSQL election with supported capture contract facts
    When a schema downgrade is attempted after a capture commits
    Then the migration and all captured evidence remain intact

  @HV-TWIN-ENT-STORAGE-006 @AC-018-010
  Scenario: Additive upgrade preserves prior elections without inventing capture evidence
    Given an owned PostgreSQL election with supported capture contract facts
    When the populated prior schema is upgraded through the entitlement migration
    Then the existing Draft is unchanged and no historical entitlement is invented

  @HV-TWIN-ENT-STORAGE-007 @AC-018-006 @AC-018-010 @AC-018-011
  Scenario: Negative Open outcomes are immutable and cannot disappear during downgrade
    Given an owned PostgreSQL election with supported capture contract facts
    When a negative Open outcome is committed and its schema downgrade is attempted
    Then negative Open evidence survives restart of the context and cannot be erased or changed
