@HushVoting @HV-E2E @E2E @HV-IDENTITY-CREATE @HV-FEAT-007
@HV-ID-RECOVERY-PROTECTION
@FEAT-007
Feature: Identity create — recovery words, protection, review
  Covers HV-ID-CREATE-RECOVERY, HV-ID-CREATE-PROTECT, HV-ID-CREATE-REVIEW.

  @FEAT-007 @AC-007-010 @HV-ID-CREATE-RECOVERY-001
  Scenario: Recovery words are displayed only through the bounded exception
    Given an active reveal authority
    When Save Recovery Words renders
    Then 24 numbered words are shown in a responsive semantic ordered layout
    And the reveal lasts at most 60 seconds per reveal

  @FEAT-007 @AC-007-011 @HV-ID-CREATE-RECOVERY-002
  Scenario: Concealment removes visual and accessibility content
    Given Alice reveals recovery words through the real Web creation authority
    When Alice exercises Web reveal timeout Back navigation pagehide regeneration and worker Lock
    Then all exercised triggers remove the recovery nodes from visual and accessibility rendering and a fresh identity can register

  @FEAT-007 @AC-007-012 @HV-ID-CREATE-RECOVERY-003
  Scenario: Copy is secondary, explicit, warned, and bounded
    Given the recovery screen is visible
    When Alice explicitly copies the visible recovery words to the real browser clipboard
    Then copy is secondary and warned and the browser clears the clipboard within thirty seconds without reading it
    And Alice can still protect and register the same candidate after clipboard cleanup

  @FEAT-007 @AC-007-013 @HV-ID-CREATE-RECOVERY-004
  Scenario: Six unpredictable distinct positions are requested in randomized order
    Given a valid candidate
    When the confirmation challenge renders
    Then six unpredictable distinct positions are requested in randomized display order
    And release builds provide no bypass

  @FEAT-007 @AC-007-014 @HV-ID-CREATE-RECOVERY-005
  Scenario: A mismatch identifies only the requested position
    Given a six-position challenge
    When one answer mismatches
    Then the error identifies only that position
    And never echoes the expected or any other word

  @FEAT-007 @AC-007-015 @HV-ID-CREATE-RECOVERY-006
  Scenario: Three failed attempts invalidate the challenge without regenerating
    Given the same candidate and challenge
    When three attempts mismatch
    Then the challenge is invalidated and protected review resumes
    And the candidate is not regenerated or exposed

  @FEAT-007 @AC-007-017 @HV-ID-CREATE-PROTECT-001
  Scenario: The Device password is collected only after recovery confirmation
    Given Alice cannot enter a device password until her real six-word recovery challenge succeeds
    When Alice protects the candidate using the direct worker secret-transfer boundary
    Then device inputs clear without exposing the password in rendered state history console or HTTP traffic and the exact identity registers

  @FEAT-007 @AC-007-018 @HV-ID-CREATE-PROTECT-002
  Scenario: Device and backup-file passwords are never confused or reused
    Given Alice reaches empty device-password fields after her real recovery challenge
    When Alice protects the new identity without entering any backup-file password
    Then creation clears device input and registers only the independently derived identity

  @FEAT-007 @AC-007-019 @HV-ID-CREATE-PROTECT-003
  Scenario: Successful validation issues one one-use authorization
    Given Alice reaches device protection with a real worker capability observer
    When Alice validates her device password and provisions the identity through the ordinary UI
    Then one successful provisioning command uses its issued purpose channel epoch and bounded lifetime
    And the real worker rejects capability replay wrong purpose and expiry without replacing the protected identity

  @FEAT-007 @AC-007-020 @HV-ID-CREATE-REVIEW-001
  Scenario: Final review contains safe fields only
    Given provisioning authorization is valid
    When Review renders
    Then normalized alias, visibility, protection/recovery state, and both abbreviated public addresses are shown
    And no private material, full address, or transaction JSON is present

  @FEAT-007 @AC-007-021 @HV-ID-CREATE-REVIEW-002
  Scenario: Create binds the reviewed fields and prevents double dispatch
    Given the review screen with a valid authorization
    When Create Identity is invoked
    Then the full reviewed fields are bound to the operation-scoped authorization
    And the action is disabled during the in-flight command with a single provisioning owner
