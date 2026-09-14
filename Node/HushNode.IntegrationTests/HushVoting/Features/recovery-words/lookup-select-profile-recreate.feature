@HushVoting @HV-E2E @E2E @HV-RECOVERY-WORDS @HV-RW-PROFILES @HV-FEAT-008
@FEAT-008
Feature: Recovery words — lookup-select-profile-recreate
  Covers HV-RW-LOOKUP, HV-RW-SELECT, HV-RW-PROFILE, HV-RW-RECREATE.

  @FEAT-008 @AC-008-024 @HV-RW-LOOKUP-001
  Scenario: AC-008-024 — HV-RW-LOOKUP
    Given Alice has registered her identity and removed its local vault
    And the HushVoting browser and actual node public lookup boundaries are observed
    When Alice restores her recovery words against the live node
    Then the only matching blockchain profile requires confirmation before protection
    And exactly 2 bounded same-origin unsigned lookups reach the node with non-cacheable replies

  @FEAT-008 @AC-008-025 @HV-RW-LOOKUP-002
  Scenario: AC-008-025 — HV-RW-LOOKUP
    Given the node has only the first of two recovered identity formats registered
    When Alice resolves the twenty-four-word identity set against the node
    Then both distinct formats have actual exact-profile or authoritative absence replies before confirmation

  @FEAT-008 @AC-008-026 @HV-RW-LOOKUP-003
  Scenario: AC-008-026 — HV-RW-LOOKUP
    Given the node will fail one of Alice's two recovery candidate lookups
    When Alice verifies her recovery phrase
    Then the partial result permits neither candidate selection nor profile creation
    And retry queries only the unresolved candidate before exposing the complete outcome

  @FEAT-008 @AC-008-027 @HV-RW-LOOKUP-004
  Scenario: AC-008-027 — HV-RW-LOOKUP
    Given the second real recovery candidate lookup stalls beyond its transport deadline
    When Alice observes the counted progress of sequential recovery lookups
    Then the unresolved lookup times out after ten seconds without becoming an absent profile

  @FEAT-008 @AC-008-028 @HV-RW-LOOKUP-005
  Scenario: AC-008-028 — HV-RW-LOOKUP
    Given Alice has registered her identity and removed its local vault
    When Alice restores her recovery words against the live node
    Then the only matching blockchain profile requires confirmation before protection
    When Alice abandons the resolved recovery and retries after reload while the node is unavailable
    Then previous profile outcomes cannot authorize recovery and only fresh live results restore Alice

  @FEAT-008 @AC-008-029 @HV-RW-SELECT-001
  Scenario: AC-008-029 — HV-RW-SELECT
    Given Alice has registered her identity and removed its local vault
    When Alice restores her recovery words against the live node
    Then the only matching blockchain profile requires confirmation before protection
    And device protection restores the same identity through fresh online verification

  @FEAT-008 @AC-008-030 @HV-RW-SELECT-002
  Scenario: AC-008-030 — HV-RW-SELECT
    Given the node has both recovered identity formats registered
    When Alice resolves the twenty-four-word identity set against the node
    Then no registered profile is selected until Alice explicitly chooses the historical identity
    And device protection activates the selected historical blockchain identity

  @FEAT-008 @AC-008-031 @HV-RW-SELECT-003
  Scenario: AC-008-031 — HV-RW-SELECT
    Given Alice owns recovery words without a blockchain profile
    When Alice selects her recovered identity for profile creation
    Then the absence explanation requires explicit creation with the recovered keys
    And registration and protection preserve the recovered signing and encryption addresses

  @FEAT-008 @AC-008-032 @HV-RW-SELECT-004
  Scenario: AC-008-032 — HV-RW-SELECT
    Given neither recovered identity format has a blockchain profile
    When Alice resolves the twenty-four-word identity set against the node
    Then the absent candidates require source-guided selection without a default

  @FEAT-008 @AC-008-033 @HV-RW-SELECT-005
  Scenario: AC-008-033 — HV-RW-SELECT
    Given neither recovered identity format has a blockchain profile
    When Alice resolves the twenty-four-word identity set against the node
    Then explicit reveal permits copying only the chosen public addresses and Hide removes them

  @FEAT-008 @AC-008-034 @HV-RW-PROFILE-001
  Scenario: AC-008-034 — HV-RW-PROFILE
    Given the first recovered signing address is registered with a different encryption key
    When Alice resolves the twenty-four-word identity set against the node
    Then a signing-only recovery match fails closed before any selection or staging

  @FEAT-008 @AC-008-035 @HV-RW-PROFILE-002
  Scenario: AC-008-035 — HV-RW-PROFILE
    Given the recovered blockchain profile has a literal markup alias and Public visibility
    When Alice resolves the twenty-four-word identity set against the node
    Then recovery shows the authoritative escaped alias and visibility without profile editing

  @FEAT-008 @AC-008-057 @HV-RW-RECREATE-001
  Scenario: AC-008-057 — HV-RW-RECREATE
    Given Alice reviewed her real recovery profile before it disappeared from the controlled node
    When final online verification finds authoritative absence after recovery protection
    Then fresh profile consent reuses the protected recovered keys and only actual indexing grants access

  @FEAT-008 @AC-008-058 @HV-RW-RECREATE-002
  Scenario: AC-008-058 — HV-RW-RECREATE
    Given Alice owns recovery words without a blockchain profile
    When Alice selects her recovered identity for profile creation
    Then the recreation alias starts empty and Private while Public requires acknowledgement
    And registration and protection preserve the recovered signing and encryption addresses

  @FEAT-008 @AC-008-059 @HV-RW-RECREATE-003
  Scenario: AC-008-059 — HV-RW-RECREATE
    Given Alice owns recovery words without a blockchain profile
    When Alice selects her recovered identity for profile creation
    Then the recreation alias starts empty and Private while Public requires acknowledgement
    And restored registration polls the unchanged node contract until exact block confirmation

  @FEAT-008 @AC-008-061 @HV-RW-RECREATE-004
  Scenario: AC-008-061 — HV-RW-RECREATE
    Given Alice starts recovery without a profile and with observed backend social storage
    When Alice selects her recovered identity for profile creation
    Then the recreation alias starts empty and Private while Public requires acknowledgement
    And recovery submits only its identity and creates no feed or social state before the separate licence handoff
