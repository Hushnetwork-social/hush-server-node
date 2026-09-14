@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-CACHE-OUTAGE-TWIN @FEAT-011
Feature: HushVoting identity truth survives real Redis outage and rejected writes
  FEAT-011 Phase 3 Tasks 3.5–3.8; FEAT-008 AC-008-076 and FEAT-009 AC-009-081.
  Backend FEAT evidence; full browser/server EPIC acceptance remains separate.

  @HV-TWIN-ID-CACHE-007 @AC-008-076 @AC-009-081
  Scenario: Stopping the real Redis process cannot hide an indexed identity
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When the run-owned Redis process is stopped during exact identity lookup
    Then restored Redis resumes exact cached lookup with one unchanged indexed identity

  @HV-TWIN-ID-CACHE-008 @AC-008-076 @AC-009-081
  Scenario: A rejected real Redis cache fill cannot turn an exact database profile into absence
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When real Redis rejects writes during an identity cache miss
    Then restored Redis resumes exact cached lookup with one unchanged indexed identity

  @HV-TWIN-ID-CACHE-009 @AC-008-076 @AC-009-081
  Scenario: A rejected real Redis TTL refresh preserves authoritative identity lookup
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When real Redis rejects writes during an identity cache hit
    Then restored Redis resumes exact cached lookup with one unchanged indexed identity
