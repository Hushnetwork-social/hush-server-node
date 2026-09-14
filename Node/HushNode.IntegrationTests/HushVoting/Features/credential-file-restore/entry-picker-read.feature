@HushVoting @HV-E2E @E2E @HV-CREDENTIAL-FILE-RESTORE @HV-FEAT-009
@FEAT-009
Feature: Credential file restore — Entry, picker, and read custody
  Covers HV-DAT-ENTRY, HV-DAT-PICKER, HV-DAT-READ, HV-DAT-TEMP, HV-DAT-SOURCE.

  @FEAT-009 @AC-009-001 @HV-DAT-ENTRY-AC001 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-001 — HV-DAT-ENTRY
    Given Alice has real staged and active vault snapshots and an independent registered backup
    When stale first-run views encounter staged active rollback removal corrupt and competing custody
    Then every fresh inspection blocks the file picker without reading or replacing credentials
    And verified empty custody permits the original backup to restore through the live node

  @FEAT-009 @AC-009-002 @HV-DAT-ENTRY-AC002 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-002 — HV-DAT-ENTRY
    Given Alice's identity is authenticated
    When Alice locks her identity and requests credential-file restoration
    Then only completed explicit removal exposes the credential-file picker

  @FEAT-009 @AC-009-003 @HV-DAT-ENTRY-AC003 @HV-DAT-WEB-CUSTODY @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-003 — HV-DAT-ENTRY
    Given the Web browser cannot create its isolated credential authority
    When credential restore is attempted with an absent and then a denied browser authority
    Then preflight blocks the picker and all reads until real authority is available and a backup restores through the live node

  @FEAT-009 @AC-009-004 @HV-DAT-PICKER-AC004 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-004 — HV-DAT-PICKER
    Given the credential picker has a real bounded backup available
    When Alice opens and cancels the single-file browser picker
    Then cancellation stays neutral and a subsequent explicit file selection can proceed

  @FEAT-009 @AC-009-005 @HV-DAT-PICKER-AC005 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-005 — HV-DAT-PICKER
    Given the credential picker has a real bounded backup available
    When Alice selects a backup with a long path-like display name and then replaces it
    Then only the bounded sanitized basename appears transiently and confirmation removes it

  @FEAT-009 @AC-009-006 @HV-DAT-PICKER-AC006 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-006 — HV-DAT-PICKER
    Given the credential picker has a real bounded backup available
    When Alice selects a valid backup without a dat extension
    Then binary validation and real identity lookup succeed independently of the extension

  @FEAT-009 @AC-009-007 @HV-DAT-READ-AC007 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-007 — HV-DAT-READ
    Given the credential picker has a real bounded backup available
    When Alice selects a valid backup without a dat extension
    Then the browser sends no backup bytes, password, or private keys to the BFF or node

  @FEAT-009 @AC-009-008 @HV-DAT-PICKER-AC008
  Scenario: AC-009-008 — HV-DAT-PICKER
    Given one source is selected through the platform picker
    When the picker outcome is projected
    Then exactly one file is accepted per attempt and cancel is neutral with no identifier shown

  @FEAT-009 @AC-009-009 @HV-DAT-PICKER-AC009
  Scenario: AC-009-009 — HV-DAT-PICKER
    Given one source is selected through the platform picker
    When the picker outcome is projected
    Then exactly one file is accepted per attempt and cancel is neutral with no identifier shown

  @FEAT-009 @AC-009-010 @HV-DAT-READ-AC010 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-010 — HV-DAT-READ
    Given Alice selects a real encrypted backup through a browser source with controlled read faults
    When the browser encounters oversized, cancelled, partial, and stalled credential reads
    Then each failed read releases its source without import and a fresh complete read reaches the live node

  @FEAT-009 @AC-009-011 @HV-DAT-READ-AC011 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-011 — HV-DAT-READ
    Given Alice selects a real encrypted backup through a browser source with controlled read faults
    When the browser encounters oversized, cancelled, partial, and stalled credential reads
    Then each failed read releases its source without import and a fresh complete read reaches the live node

  @FEAT-009 @AC-009-012 @HV-DAT-TEMP-AC012
  Scenario: AC-009-012 — HV-DAT-TEMP
    Given an unavoidable temporary ciphertext copy exists
    When cleanup runs on the current path
    Then app-private no-backup storage is used and verified cleanup covers every path and startup

  @FEAT-009 @AC-009-035 @HV-DAT-SOURCE-AC035 @HV-DAT-WEB-CUSTODY @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-035 — HV-DAT-SOURCE
    Given Alice selects her real registered credential source with an import-release observer
    When the first post-validation identity request is paused before reaching the live node
    Then released import material cannot be reused and the unchanged source restores through real identity and licence verification

  @FEAT-009 @AC-009-036 @HV-DAT-SOURCE-AC036 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-036 — HV-DAT-SOURCE
    Given Alice selects a real unchanged source file containing her encrypted registered keys and recovery words
    When Alice cancels an import and fails backup authentication without changing the original file
    And Alice decrypts the backup with its exact untrimmed UTF-8 password
    Then the browser decrypts the approved PBKDF2 and AES-GCM envelope without altering its keys
    And separate device protection restores that identity through the live node
    And the original file remains byte for byte unchanged and the verified root explains recovery words were not retained

  @FEAT-009 @AC-009-037 @HV-DAT-SOURCE-AC037 @HV-DAT-ENTRY-MIGRATED
  Scenario: AC-009-037 — HV-DAT-SOURCE
    Given Alice selects a real unchanged source file containing her encrypted registered keys and recovery words
    When Alice decrypts the backup with its exact untrimmed UTF-8 password
    Then the browser decrypts the approved PBKDF2 and AES-GCM envelope without altering its keys
    And separate device protection restores that identity through the live node
    And browser persistence contains no credential source copy filename path or source digest after restoration
