@HushVoting @HV-E2E @E2E @HV-MISSING-PROFILE-CONSENT @HV-FEAT-010
Feature: Returning HushVoting missing-profile consent and local custody
  Supplemental regression for FEAT-010 AC-010-025/040 and FEAT-002's explicit
  confirmation contract. This does not qualify full blockchain reset/recreation.

  @FEAT-010 @AC-010-025 @AC-010-040 @HV-AUTH-MISSING-CONSENT-001
  Scenario: Authoritative profile absence waits for consent and Back locks the existing vault
    Given a returning HushVoting vault whose own indexed profile has disappeared
    When returning unlock finds real profile absence before creation consent
    Then only explicit confirmation invokes the missing-profile action
    And Back completes the worker lock and preserves the returning vault
