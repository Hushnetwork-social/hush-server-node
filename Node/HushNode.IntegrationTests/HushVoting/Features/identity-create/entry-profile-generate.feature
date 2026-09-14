@HushVoting @HV-E2E @E2E @HV-IDENTITY-CREATE @HV-FEAT-007
@FEAT-007
Feature: Identity create — entry, preflight, profile, generation
  Covers HV-ID-CREATE-ENTRY, HV-ID-CREATE-PROFILE, HV-ID-CREATE-GENERATE.

  @FEAT-007 @AC-007-001 @HV-ID-CREATE-ENTRY-001 @HV-ID-ENTRY-MIGRATED
  Scenario: The no-local-user entry offers exactly three equal primary choices
    Given there is no local user
    When the first-run entry renders
    Then Create User, Restore Credential File, and Restore Recovery Words are shown with equal primary weight
    And no password field exists anywhere on the entry

  @FEAT-007 @AC-007-002 @HV-ID-CREATE-ENTRY-002 @HV-ID-ENTRY-MIGRATED
  Scenario: Create User runs the platform security preflight before collecting anything
    Given Web creation starts with controlled unavailable and denied shared-worker capability
    When creation preflight and explicit Retry encounter those unavailable Web authorities
    Then alias and secrets remain absent until real Web preflight passes and Alice creates through the live node

  @FEAT-007 @AC-007-063 @HV-ID-CREATE-ENTRY-003
  Scenario: The browser adapter is the only web storage authority
    # Web realization of AC-007-063; native static-build exclusion remains separately unqualified.
    Given the ordinary Web creation worker and page storage boundary are observed
    When Alice creates protects and activates an identity through that Web authority
    Then the Web worker owns encrypted custody and real node requests use the same-origin BFF

  @FEAT-007 @AC-007-068 @HV-ID-CREATE-ENTRY-004 @HV-ACCESSIBILITY
  Scenario: Entry and profile surfaces meet WCAG 2.2 AA and responsive rules
    # Automated Web checkpoint evidence; full keyboard/zoom/focus/screen-reader qualification remains pending.
    Given Alice checks Web accessibility across real identity creation and server activation
    Then the measured Web checkpoints have no automated accessibility or layout findings

  @FEAT-007 @AC-007-003 @HV-ID-CREATE-PROFILE-001 @HV-ID-ENTRY-MIGRATED
  Scenario: Alias normalization follows the exact profile contract
    Given a new alias with outer Unicode whitespace
    When profile validation runs
    Then the alias is trimmed, normalized to NFC, and accepted within 1-64 graphemes and 256 UTF-8 bytes
    And disallowed controls, bidi, and unsafe invisible characters are rejected

  @FEAT-007 @AC-007-004 @HV-ID-CREATE-PROFILE-002 @HV-ID-ENTRY-MIGRATED
  Scenario: Private is the default and Public requires acknowledgement
    Given the Profile screen renders
    When the user reviews visibility
    Then Private is selected by default
    And choosing Public shows a plain-language exposure/permanence warning requiring explicit acknowledgement

  @FEAT-007 @AC-007-051 @HV-ID-CREATE-PROFILE-003 @HV-ID-ENTRY-MIGRATED
  Scenario: Initial visibility is immutable through the current interface
    Given Alice explicitly creates and authenticates a Public identity through HushVoting
    When a second device submits the same signed identity keys with changed alias and Private visibility
    Then the indexed Public profile stays unchanged and HushVoting offers no duplicate-submission visibility update

  @FEAT-007 @AC-007-005 @HV-ID-CREATE-GENERATE-001 @HV-ID-ENTRY-MIGRATED
  Scenario: No password participates in identity derivation
    Given an active reveal authority
    When Alice protects and registers that generated identity
    Then the encrypted vault and live profile match independent derivation from the words alone

  @FEAT-007 @AC-007-006 @HV-ID-CREATE-GENERATE-002 @HV-ID-ENTRY-MIGRATED
  Scenario: The authority creates exactly one valid P-01 candidate
    Given Alice awaits fresh creation custody before reaching generation with a candidate operation counter
    When Alice double-clicks the real Generate recovery words control
    Then one worker candidate supplies a valid 24-word P-01 identity that survives protection and live registration

  @FEAT-007 @AC-007-007 @HV-ID-CREATE-GENERATE-003 @HV-ID-ENTRY-MIGRATED
  Scenario: Hidden invalid candidates are destroyed before display
    Given Alice is ready to generate before a controlled hidden worker derivation failure
    When three failed hidden attempts reveal nothing and a fresh attempt recovers from one derivation failure
    Then only the regenerated valid candidate can be protected and registered with real identity and licence indexing

  @FEAT-007 @AC-007-008 @HV-ID-CREATE-GENERATE-004 @HV-ID-ENTRY-MIGRATED
  Scenario: Regeneration requires destructive confirmation and never substitutes silently
    Given words have been displayed for a candidate
    When the user requests Regenerate
    Then a destructive confirmation is required
    And the complete old candidate is destroyed, confirmation state resets, and a wholly new candidate is created

  @FEAT-007 @AC-007-009 @HV-ID-CREATE-GENERATE-005
  Scenario: Generation timing meets the documented budgets
    Given explicit generation starts
    When progress becomes visible
    Then progress appears after 150 ms
    And generation completes within 1 second on the minimum supported class, never exceeding the 10 second hard bound

  @FEAT-007 @AC-007-074 @HV-ID-CREATE-GENERATE-006
  Scenario: Performance budgets never weaken cryptography or skip validation
    Given the generation and KDF gates run
    When resource limits are applied
    Then timing budgets pass without skipping lookup, weakening cryptography, or increasing secret exposure
