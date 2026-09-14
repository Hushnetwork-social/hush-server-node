@HushVoting @HV-E2E @E2E @HV-RECOVERY-WORDS @HV-FEAT-008
@FEAT-008
Feature: Recovery words — protection-staging-session
  Covers HV-RW-PASSWORD, HV-RW-PASSKEY, HV-RW-NATIVE-PASSWORDLESS, HV-RW-SESSION, HV-RW-STAGE.

  @FEAT-008 @AC-008-036 @HV-RW-PASSWORD-001 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-036 — HV-RW-PASSWORD
    Given Alice has confirmed her registered recovery profile and reached device protection
    When Alice enters valid device passwords without acknowledging recovery-word non-retention
    Then recovery cannot stage credentials before explicit non-retention acknowledgement
    And device protection restores the same identity through fresh online verification

  @FEAT-008 @AC-008-037 @HV-RW-STAGE-001
  Scenario: AC-008-037 — HV-RW-STAGE
    Given Alice has an external recovery phrase for a real registered identity and an empty local vault
    When recovery retains its selected candidate until actual encrypted read back completes then discards it before activation
    Then only the selected encrypted keys remain and fresh real node verification permits access

  @FEAT-008 @AC-008-038 @HV-RW-SESSION-001
  Scenario: AC-008-038 — HV-RW-SESSION
    Given explicit session-only selection
    When the session authority is issued
    Then nothing persists and recovery is required after authority loss

  @FEAT-008 @AC-008-040 @HV-RW-PASSWORD-002
  Scenario: AC-008-040 — HV-RW-PASSWORD
    Given the protection screen
    When the user chooses protection
    Then Device-password is checked by default and secrets enter the authority directly

  @FEAT-008 @AC-008-041 @HV-RW-PASSWORD-003 @HV-RW-PASSWORD-HIERARCHY
  Scenario: AC-008-041 — HV-RW-PASSWORD
    Given Alice confirms her recovered exact profile before choosing a Unicode device password
    When the Web password wraps the recovered vault key and the real node verifies unchanged identity
    Then after browser process loss the canonically equivalent password unlocks the same recovered identity

  @FEAT-008 @AC-008-042 @HV-RW-PASSKEY-001
  Scenario: AC-008-042 — HV-RW-PASSKEY
    Given passwordless Web selection
    When WebAuthn PRF qualification is evaluated
    Then qualified platform/PRF/RP checks gate persistence and failures offer no silent fallback

  @FEAT-008 @AC-008-043 @HV-RW-PASSKEY-002
  Scenario: AC-008-043 — HV-RW-PASSKEY
    Given passwordless Web selection
    When WebAuthn PRF qualification is evaluated
    Then qualified platform/PRF/RP checks gate persistence and failures offer no silent fallback

  @FEAT-008 @AC-008-044 @HV-RW-PASSKEY-003
  Scenario: AC-008-044 — HV-RW-PASSKEY
    Given passwordless Web selection
    When WebAuthn PRF qualification is evaluated
    Then qualified platform/PRF/RP checks gate persistence and failures offer no silent fallback

  @FEAT-008 @AC-008-045 @HV-RW-PASSKEY-004
  Scenario: AC-008-045 — HV-RW-PASSKEY
    Given passwordless Web selection
    When WebAuthn PRF qualification is evaluated
    Then qualified platform/PRF/RP checks gate persistence and failures offer no silent fallback

  @FEAT-008 @AC-008-046 @HV-RW-PASSKEY-005
  Scenario: AC-008-046 — HV-RW-PASSKEY
    Given passwordless Web selection
    When WebAuthn PRF qualification is evaluated
    Then qualified platform/PRF/RP checks gate persistence and failures offer no silent fallback

  @FEAT-008 @AC-008-047 @HV-RW-PASSKEY-006
  Scenario: AC-008-047 — HV-RW-PASSKEY
    Given passwordless Web selection
    When WebAuthn PRF qualification is evaluated
    Then qualified platform/PRF/RP checks gate persistence and failures offer no silent fallback

  @FEAT-008 @AC-008-048 @HV-RW-NATIVE-PASSWORDLESS-001
  Scenario: AC-008-048 — HV-RW-NATIVE-PASSWORDLESS
    Given a native platform
    When passwordless native protection is selected
    Then qualified Secret Service or hardware-backed Keystore gates persistence and warns honestly

  @FEAT-008 @AC-008-049 @HV-RW-NATIVE-PASSWORDLESS-002
  Scenario: AC-008-049 — HV-RW-NATIVE-PASSWORDLESS
    Given a native platform
    When passwordless native protection is selected
    Then qualified Secret Service or hardware-backed Keystore gates persistence and warns honestly

  @FEAT-008 @AC-008-050 @HV-RW-NATIVE-PASSWORDLESS-003
  Scenario: AC-008-050 — HV-RW-NATIVE-PASSWORDLESS
    Given a native platform
    When passwordless native protection is selected
    Then qualified Secret Service or hardware-backed Keystore gates persistence and warns honestly

  @FEAT-008 @AC-008-051 @HV-RW-SESSION-002 @HV-SESSION-ONLY
  Scenario: AC-008-051 — HV-RW-SESSION
    Given Alice restores her registered identity from words to choose a temporary Web session
    When Alice explicitly chooses and acknowledges session-only protection without a device password
    Then browser process loss leaves no remembered session identity and requires recovery input again

  @FEAT-008 @AC-008-053 @HV-RW-STAGE-002 @HV-STAGE-INTEGRITY @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-053 — HV-RW-STAGE
    Given Alice selects her real registered recovery candidate for atomic password protected staging
    When recovery waits for its actual IndexedDB read back before committing the selected keys
    Then only the selected encrypted recovery keys survive and fresh node verification activates them
    When Alice restores again and the actual recovery journal transaction aborts
    Then an aborted recovery commit leaves no active journal and cannot start online activation

  @FEAT-008 @AC-008-054 @HV-RW-STAGE-003 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-054 — HV-RW-STAGE
    Given Alice has confirmed her registered recovery profile and reached device protection
    When the node becomes unavailable after recovery review and Alice stages protection
    Then the staged recovery stays unauthenticated and only its bounded locked preview survives restart
    And the staged recovered identity activates only after connectivity returns

  @FEAT-008 @AC-008-055 @HV-RW-STAGE-004 @HV-RW-CUSTODY-STAGING-MIGRATED
  Scenario: AC-008-055 — HV-RW-STAGE
    Given Alice has confirmed her registered recovery profile and reached device protection
    When device protection restores the same identity through fresh online verification
    Then activation has freshly verified both recovered public keys against the node

  @FEAT-008 @AC-008-060 @HV-RW-SESSION-003
  Scenario: AC-008-060 — HV-RW-SESSION
    Given explicit session-only selection
    When the session authority is issued
    Then nothing persists and recovery is required after authority loss

  @FEAT-008 @AC-008-081 @HV-RW-PASSKEY-007 @HV-EXTERNAL-QUALIFICATION
  Scenario: AC-008-081 — HV-RW-PASSKEY
    Given the supported current and previous browser majors and every claimed operating system class
    When real-device WebAuthn PRF qualification evidence is evaluated
    Then every claimed device and browser qualification must have its own admissible result
    And virtual-authenticator Web runs do not satisfy this external gate
