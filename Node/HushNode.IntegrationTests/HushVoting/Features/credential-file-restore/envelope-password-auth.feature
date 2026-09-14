@HushVoting @HV-E2E @E2E @HV-CREDENTIAL-FILE-RESTORE @HV-FEAT-009
@FEAT-009 @HV-DAT-ENVELOPE-AUTH
Feature: Credential file restore — Envelope, password, and backoff
  Covers HV-DAT-ENVELOPE, HV-DAT-PASSWORD, HV-DAT-BACKOFF, HV-DAT-AUTH.

  @FEAT-009 @AC-009-013 @HV-DAT-ENVELOPE-AC013
  Scenario: AC-009-013 — HV-DAT-ENVELOPE
    Given a HUSH v1 envelope is inspected
    When the structural gate runs before password use
    Then magic, little-endian version one, salt, nonce, and ciphertext bounds are validated with safe pre-password errors

  @FEAT-009 @AC-009-014 @HV-DAT-ENVELOPE-AC014
  Scenario: AC-009-014 — HV-DAT-ENVELOPE
    Given a HUSH v1 envelope is inspected
    When the structural gate runs before password use
    Then magic, little-endian version one, salt, nonce, and ciphertext bounds are validated with safe pre-password errors

  @FEAT-009 @AC-009-015 @HV-DAT-ENVELOPE-AC015
  Scenario: AC-009-015 — HV-DAT-ENVELOPE
    Given Alice has an independently encrypted HUSH v1 backup of her registered identity
    When Alice decrypts the backup with its exact untrimmed UTF-8 password
    Then the browser decrypts the approved PBKDF2 and AES-GCM envelope without altering its keys
    And separate device protection restores that identity through the live node

  @FEAT-009 @AC-009-016 @HV-DAT-PASSWORD-AC016
  Scenario: AC-009-016 — HV-DAT-PASSWORD
    Given the Backup-file password field is ready
    When Alice decrypts independently encrypted backups at the raw UTF-8 password boundaries
    Then short and unnormalized passwords reach real identity lookup and oversized passwords do not

  @FEAT-009 @AC-009-017 @HV-DAT-PASSWORD-AC017
  Scenario: AC-009-017 — HV-DAT-PASSWORD
    Given the Backup-file password field is ready
    When Alice explicitly confirms that her backup was created without a password
    Then the empty-password backup decrypts only after unchecked-by-default consent and a warning

  @FEAT-009 @AC-009-018 @HV-DAT-PASSWORD-AC018
  Scenario: AC-009-018 — HV-DAT-PASSWORD
    Given the Backup-file password field is ready
    When Alice pastes and explicitly reveals and conceals her backup password
    Then the labelled password field keeps its backup-only purpose and decrypts through the real worker

  @FEAT-009 @AC-009-019 @HV-DAT-AUTH-AC019
  Scenario: AC-009-019 — HV-DAT-AUTH
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice attempts wrong-password, damaged-ciphertext, and damaged-tag imports
    Then every authentication failure uses the same ambiguous message and never queries or stages an identity

  @FEAT-009 @AC-009-020 @HV-DAT-BACKOFF-AC020
  Scenario: AC-009-020 — HV-DAT-BACKOFF
    Given a real credential backup is awaiting password authentication
    When Alice submits seven incorrect passwords through the real worker
    Then the first two failures have no extra delay and the next delays are exactly two four eight sixteen and thirty seconds

  @FEAT-009 @AC-009-021 @HV-DAT-BACKOFF-AC021
  Scenario: AC-009-021 — HV-DAT-BACKOFF
    Given a real credential backup is awaiting password authentication
    When Alice changes files and then moves restoration to another tab during an active delay
    Then the worker preserves backoff across files and tabs and complete validation resets its failure counter

  @FEAT-009 @AC-009-022 @HV-DAT-AUTH-AC022
  Scenario: AC-009-022 — HV-DAT-AUTH
    Given Alice selects one real encrypted backup for repeated failed authentication and same-epoch Retry
    When two wrong passwords fail without selecting or transferring another file
    Then only a fresh password can reuse the bounded ciphertext and restore the same keys through live identity and licence verification
