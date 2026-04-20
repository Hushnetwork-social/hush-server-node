@Integration @FEAT-107 @FEAT-105 @ElectionCrypto @NON_E2E
Feature: Election crypto cross-repo interop
  As a FEAT-107 maintainer
  I want a narrow non-E2E client/server interop slice
  So that deterministic election crypto fixtures can move across the web and server boundary safely

  @AT-107-I01 @AT-107-I02 @AT-107-I08 @AT-107-I10
  Scenario Outline: Accepted FEAT-107 fixture packs remain interoperable across supported profiles
    Given FEAT-107 controlled election fixture infrastructure is available
    When the client generates controlled fixture pack "<fixtureName>" with seed "<seed>" choice "<choiceIndex>" profile "<profile>" tier "<decodeTier>" and version "<fixtureVersion>"
    And the server harness loads and validates that fixture pack
    Then the fixture version policy should report "<expectedVersionStatus>"
    And the loaded fixture profile should be "<profile>"
    And the loaded fixture circuit version should be "<circuitVersion>"
    And the server should accept the loaded ballot structure
    And the server should derive the same ballot meaning as the client fixture
    And the server should derive the expected tally meaning from the rerandomized ballot

    Examples:
      | fixtureName     | seed | choiceIndex | profile                 | decodeTier        | fixtureVersion | expectedVersionStatus | circuitVersion    |
      | dev-supported   | 1701 | 2           | DEV_SMOKE_PROFILE       | DEV_SMOKE_TIER    | feat-107.v1    | supported             | dev-smoke-fixture |
      | prod-supported  | 2701 | 4           | PRODUCTION_LIKE_PROFILE | CLUB_ROLLOUT_TIER | feat-107.v1    | supported             | omega-v1.0.0      |
      | prod-deprecated | 3701 | 1           | PRODUCTION_LIKE_PROFILE | CLUB_ROLLOUT_TIER | feat-107.v0    | deprecated            | omega-v1.0.0      |

  @AT-107-I11
  Scenario: Vulnerable FEAT-107 fixture packs are rejected before tally interpretation
    Given FEAT-107 controlled election fixture infrastructure is available
    When the client generates controlled fixture pack "prod-vulnerable" with seed "4701" choice "3" profile "PRODUCTION_LIKE_PROFILE" tier "CLUB_ROLLOUT_TIER" and version "feat-107.v0-broken"
    And the server harness loads and validates that fixture pack
    Then the fixture version policy should report "vulnerable"
    And the server should refuse further ballot interpretation
