@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-CACHE-TWIN @FEAT-011
Feature: HushVoting identity cache truth and real Redis failure fallback
  FEAT-011 Phase 3 Tasks 3.5/3.6 and 3.7/3.8; shared FEAT-048 contract.
  Supporting FEAT-008/009 backend evidence, not EPIC browser acceptance.

  @HV-TWIN-ID-CACHE-001 @AC-008-076 @AC-009-081
  Scenario: Repeated absence cannot hide a newly indexed exact identity
    Given the owned real node has an unregistered identity and its actual Redis cache
    When repeated exact absence lookups precede real admission and indexing
    Then the exact indexed profile is cached and retry creates no duplicate state

  @HV-TWIN-ID-CACHE-002 @AC-008-076 @AC-009-081
  Scenario: Real Redis invalidation preserves authoritative identity lookup
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When the owned identity cache entry encounters invalidation
    Then normal identity RPC falls back to PostgreSQL and repairs the cache without admission

  @HV-TWIN-ID-CACHE-003 @AC-008-076 @AC-009-081
  Scenario: Real Redis malformed-json preserves authoritative identity lookup
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When the owned identity cache entry encounters malformed-json
    Then normal identity RPC falls back to PostgreSQL and repairs the cache without admission

  @HV-TWIN-ID-CACHE-004 @AC-008-076 @AC-009-081
  Scenario: Real Redis wrong-redis-type preserves authoritative identity lookup
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When the owned identity cache entry encounters wrong-redis-type
    Then normal identity RPC falls back to PostgreSQL and repairs the cache without admission

  @HV-TWIN-ID-CACHE-005 @AC-008-076 @AC-009-081
  Scenario: A parseable cache profile with no alias cannot break identity RPC
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When the owned identity cache entry encounters null-alias
    Then normal identity RPC falls back to PostgreSQL and repairs the cache without admission

  @HV-TWIN-ID-CACHE-006 @AC-008-076 @AC-009-081
  Scenario: A cache profile for another signing address cannot override exact lookup
    Given the owned real node has an unregistered identity and its actual Redis cache
    And the identity is genuinely indexed and its exact profile has populated Redis
    When the owned identity cache entry encounters wrong-signing-address
    Then normal identity RPC falls back to PostgreSQL and repairs the cache without admission
