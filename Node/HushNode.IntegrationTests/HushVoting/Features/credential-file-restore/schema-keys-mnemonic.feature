@HushVoting @HV-E2E @E2E @HV-CREDENTIAL-FILE-RESTORE @HV-FEAT-009
@FEAT-009
Feature: Credential file restore — Strict schema, key proof, and mnemonic
  Covers HV-DAT-SCHEMA, HV-DAT-KEYS, HV-DAT-MNEMONIC.

  @FEAT-009 @AC-009-023 @HV-DAT-SCHEMA-AC023 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-023 — HV-DAT-SCHEMA
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice imports authenticated JSON with duplicate, unknown, missing, invalid-type, and out-of-bounds fields
    Then invalid credential data is rejected before lookup and a consistent backup reaches the live node

  @FEAT-009 @AC-009-024 @HV-DAT-KEYS-AC024 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-024 — HV-DAT-KEYS
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice imports a backup whose signing private key disagrees with its public address
    Then invalid credential data is rejected before lookup and a consistent backup reaches the live node

  @FEAT-009 @AC-009-025 @HV-DAT-KEYS-AC025 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-025 — HV-DAT-KEYS
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice imports a backup whose encryption private key disagrees with its public address
    Then invalid credential data is rejected before lookup and a consistent backup reaches the live node

  @FEAT-009 @AC-009-026 @HV-DAT-KEYS-AC026
  Scenario: AC-009-026 — HV-DAT-KEYS
    Given concrete signing and encryption pairs are present
    When local key-control proof runs
    Then both private keys independently derive exact stored public addresses and pass domain-separated consistency checks before lookup

  @FEAT-009 @AC-009-027 @HV-DAT-MNEMONIC-AC027 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-027 — HV-DAT-MNEMONIC
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice imports a backup with a valid phrase that derives different concrete keys
    Then invalid credential data is rejected before lookup and a consistent backup reaches the live node

  @FEAT-009 @AC-009-028 @HV-DAT-MNEMONIC-AC028 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-028 — HV-DAT-MNEMONIC
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice decrypts a valid internally consistent backup with a null mnemonic
    Then the decrypted candidate still requires explicit profile confirmation and separate protection

  @FEAT-009 @AC-009-029 @HV-DAT-KEYS-AC029 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-029 — HV-DAT-KEYS
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice imports backups with malformed or unsupported key encodings
    Then invalid credential data is rejected before lookup and a consistent backup reaches the live node

  @FEAT-009 @AC-009-030 @HV-DAT-KEYS-AC030 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-030 — HV-DAT-KEYS
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice imports schema, key-pair, and mnemonic inconsistencies
    Then the visible inconsistency message is shared while safe internal failure codes remain distinct

  @FEAT-009 @AC-009-031 @HV-DAT-SCHEMA-AC031 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-031 — HV-DAT-SCHEMA
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice imports schema, key-pair, and mnemonic inconsistencies
    Then the visible inconsistency message is shared while safe internal failure codes remain distinct
    And semantic failures expose no decrypted values and a fresh valid import uses only its own identity

  @FEAT-009 @AC-009-032 @HV-DAT-KEYS-AC032 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-032 — HV-DAT-KEYS
    Given independently authenticated credential payloads are ready for the real browser worker
    When Alice decrypts a valid internally consistent backup with a null mnemonic
    Then the decrypted candidate still requires explicit profile confirmation and separate protection

  @FEAT-009 @AC-009-033 @HV-DAT-MNEMONIC-AC033 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-033 — HV-DAT-MNEMONIC
    Given Alice has an independently encrypted registered backup containing its matching legacy recovery words
    When Alice decrypts the backup with its exact untrimmed UTF-8 password
    Then the browser decrypts the approved PBKDF2 and AES-GCM envelope without altering its keys
    And separate device protection restores that identity through the live node
    And independent inspection and a fresh unlock find only the imported concrete keys and no recovery reveal

  @FEAT-009 @AC-009-034 @HV-DAT-MNEMONIC-AC034 @HV-DAT-VALIDATION-MIGRATED
  Scenario: AC-009-034 — HV-DAT-MNEMONIC
    Given Alice selects a real unchanged source file containing her encrypted registered keys and recovery words
    When Alice decrypts the backup with its exact untrimmed UTF-8 password
    Then the browser decrypts the approved PBKDF2 and AES-GCM envelope without altering its keys
    And separate device protection restores that identity through the live node
    And the original file remains byte for byte unchanged and the verified root explains recovery words were not retained
