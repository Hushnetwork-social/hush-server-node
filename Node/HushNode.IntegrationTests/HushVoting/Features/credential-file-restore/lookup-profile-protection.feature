@HushVoting @HV-E2E @E2E @HV-CREDENTIAL-FILE-RESTORE @HV-FEAT-009
@FEAT-009
Feature: Credential file restore — Lookup, profile, protection, and staging
  Covers HV-DAT-LOOKUP, HV-DAT-RESET, HV-DAT-SIGNATURE, HV-DAT-SEPARATION, HV-DAT-PROTECT, HV-DAT-STAGE, HV-DAT-SESSION, HV-DAT-RESUME.

  @FEAT-009 @AC-009-038 @HV-DAT-LOOKUP-AC038 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-038 — HV-DAT-LOOKUP
    Given independently authenticated credential payloads are ready for the real browser worker
    And the HushVoting browser and actual node public lookup boundaries are observed
    When Alice imports schema, key-pair, and mnemonic inconsistencies
    Then the visible inconsistency message is shared while safe internal failure codes remain distinct
    And invalid credential data is rejected before lookup and a consistent backup reaches the live node
    And exactly 1 bounded same-origin unsigned lookups reach the node with non-cacheable replies
    And the decrypted candidate still requires explicit profile confirmation and separate protection

  @FEAT-009 @AC-009-039 @HV-DAT-LOOKUP-AC039 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-039 — HV-DAT-LOOKUP
    Given the node has the backup signing address registered with a different valid encryption address
    When Alice decrypts the backup and performs its real public identity lookup
    Then a signing-only match cannot authorize protection, registration, or authentication

  @FEAT-009 @AC-009-040 @HV-DAT-LOOKUP-AC040 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-040 — HV-DAT-LOOKUP
    Given the encrypted backup contains an old alias and Public visibility for Alice's Private blockchain identity
    When Alice restores authoritative backup metadata while real Redis rejects cache fills
    Then the encrypted local metadata uses the blockchain alias and visibility without changing its profile

  @FEAT-009 @AC-009-041 @HV-DAT-LOOKUP-AC041 @HV-DAT-HISTORICAL-ALIAS
  Scenario: AC-009-041 — HV-DAT-LOOKUP
    Given an indexed credential identity has historical markup and unsafe controls in its authoritative alias
    When credential restoration reaches the real entitlement gate and workspace with the historical profile
    Then only displayed controls are replaced while a gross oversized profile cannot enable another import

  @FEAT-009 @AC-009-042 @HV-DAT-LOOKUP-AC042 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-042 — HV-DAT-LOOKUP
    Given the credential identity lookup service is temporarily unavailable
    When Alice decrypts the backup and performs its real public identity lookup
    Then transport failure offers no profile creation and only a later authoritative absence enables review

  @FEAT-009 @AC-009-043 @HV-DAT-RESET-AC043 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-043 — HV-DAT-RESET
    Given Alice decrypts a public backup whose identity is absent from the live blockchain
    When Alice reviews the missing-profile explanation and both recovered public addresses
    Then the review preserves the same keys and bound network while waiting for explicit profile consent

  @FEAT-009 @AC-009-044 @HV-DAT-RESET-AC044 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-044 — HV-DAT-RESET
    Given Alice decrypts a public backup whose identity is absent from the live blockchain
    When Alice corrects the imported profile name and explicitly acknowledges Public visibility
    Then separate protection registers only the reviewed profile using the exact imported keys

  @FEAT-009 @AC-009-045 @HV-DAT-RESET-AC045 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-045 — HV-DAT-RESET
    Given Alice decrypts a public backup whose identity is absent from the live blockchain
    When Alice reviews the missing-profile explanation and both recovered public addresses
    Then the review preserves the same keys and bound network while waiting for explicit profile consent

  @FEAT-009 @AC-009-046 @HV-DAT-SIGNATURE-AC046 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-046 — HV-DAT-SIGNATURE
    Given Alice decrypts a public backup whose identity is absent from the live blockchain
    When Alice corrects the imported profile name and explicitly acknowledges Public visibility
    Then separate protection registers only the reviewed profile using the exact imported keys

  @FEAT-009 @AC-009-047 @HV-DAT-SIGNATURE-AC047 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-047 — HV-DAT-SIGNATURE
    Given Alice decrypts a public backup whose identity is absent from the live blockchain
    When the node receives the explicitly confirmed imported profile with a damaged signature
    Then unsigned lookup absence remains distinct from the server's typed invalid-signature rejection

  @FEAT-009 @AC-009-048 @HV-DAT-SIGNATURE-AC048 @HV-DAT-LOOKUP-MIGRATED
  Scenario: AC-009-048 — HV-DAT-SIGNATURE
    Given Alice decrypts a public backup whose identity is absent from the live blockchain
    When Alice corrects the imported profile name and explicitly acknowledges Public visibility
    Then separate protection registers only the reviewed profile using the exact imported keys

  @FEAT-009 @AC-009-049 @HV-DAT-SEPARATION-AC049 @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-049 — HV-DAT-SEPARATION
    Given Alice has decrypted her registered backup and reached separate device protection
    When Alice protects the restored keys with a separately entered device password
    Then the backup password cannot unlock the restored vault and only the new device password succeeds

  @FEAT-009 @AC-009-050 @HV-DAT-SEPARATION-AC050 @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-050 — HV-DAT-SEPARATION
    Given Alice has decrypted her registered backup and reached separate device protection
    When Alice protects the restored keys with a separately entered device password
    Then the backup password cannot unlock the restored vault and only the new device password succeeds

  @FEAT-009 @AC-009-051 @HV-DAT-PROTECT-AC051 @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-051 — HV-DAT-PROTECT
    Given Alice has decrypted her registered backup and reached separate device protection
    When Alice confirms a separately entered password in the default device protection mode
    Then independent decryption verifies the password wrapped concrete keys and live restoration
    And the backup password cannot unlock the restored vault and only the new device password succeeds

  @FEAT-009 @AC-009-052 @HV-DAT-PROTECT-AC052
  Scenario: AC-009-052 — HV-DAT-PROTECT
    Given protection choices are available
    When a mode is selected
    Then Device-password is default and only qualified passwordless or explicit session-only alternatives are representable

  @FEAT-009 @AC-009-053 @HV-DAT-PROTECT-AC053
  Scenario: AC-009-053 — HV-DAT-PROTECT
    Given protection choices are available
    When a mode is selected
    Then Device-password is default and only qualified passwordless or explicit session-only alternatives are representable

  @FEAT-009 @AC-009-054 @HV-DAT-SESSION-AC054 @HV-SESSION-ONLY
  Scenario: AC-009-054 — HV-DAT-SESSION
    Given Alice restores her registered identity from a credential file to choose a temporary Web session
    When Alice explicitly chooses and acknowledges session-only protection without a device password
    Then browser process loss leaves no remembered session identity and requires recovery input again

  @FEAT-009 @AC-009-055 @HV-DAT-PROTECT-AC055 @HV-PROTECTION-METADATA @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-055 — HV-DAT-PROTECT
    Given Alice has imported and locked a real password-protected credential-file vault
    When closed imported metadata encounters unknown downgraded native unwrapped and future-version faults
    Then only the unchanged password-protected record and fresh exact verification restore access


  @FEAT-009 @AC-009-056 @HV-DAT-STAGE-AC056 @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-056 — HV-DAT-STAGE
    Given Alice has decrypted her registered backup and reached separate device protection
    When the node becomes unavailable after import lookup and Alice stages device protection
    Then independent decryption finds only the exact concrete keys and authenticated profile network and protection metadata in the pending vault
    And staged credentials never authenticate offline and recovery succeeds only after a fresh online lookup

  @FEAT-009 @AC-009-057 @HV-DAT-STAGE-AC057 @HV-STAGE-INTEGRITY @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-057 — HV-DAT-STAGE
    Given Alice imports her real registered source and reaches the atomic staging boundary
    When the worker waits for the actual encrypted slot read back before switching its journal
    Then the exact encrypted stage stays pending until fresh real node verification activates it
    When Alice restores again and ciphertext changes during the worker's actual IndexedDB write
    Then changed staged bytes cannot commit a journal or initiate online activation

  @FEAT-009 @AC-009-058 @HV-DAT-STAGE-AC058 @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-058 — HV-DAT-STAGE
    Given Alice has decrypted her registered backup and reached separate device protection
    When the node becomes unavailable after import lookup and Alice stages device protection
    Then staged credentials never authenticate offline and recovery succeeds only after a fresh online lookup

  @FEAT-009 @AC-009-059 @HV-DAT-STAGE-AC059 @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-059 — HV-DAT-STAGE
    Given Alice decrypts a public backup whose identity is absent from the live blockchain
    When Alice corrects the imported profile name and explicitly acknowledges Public visibility
    Then separate protection registers only the reviewed profile using the exact imported keys

  @FEAT-009 @AC-009-060 @HV-DAT-SESSION-AC060
  Scenario: AC-009-060 — HV-DAT-SESSION
    Given session-only is selected
    When the session authority ends
    Then no local user, stage, or transaction persists and exact online verification is required again

  @FEAT-009 @AC-009-061 @HV-DAT-STAGE-AC061 @HV-STAGED-RESUME
  Scenario: AC-009-061 — HV-DAT-STAGE
    Given Alice has genuinely staged a credential file in a browser that will lose its process
    When the browser restarts with only its persistent staged vault and no onboarding authority
    Then the required staged-resume heading appears before password unlock and exact online activation

  @FEAT-009 @AC-009-062 @HV-DAT-RESUME-AC062 @HV-RESUME-LOOKUP @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-062 — HV-DAT-RESUME
    Given Alice stages imported credentials in a restartable browser before its source becomes unavailable
    When the browser crashes and credential restoration restarts without the source or backup password
    Then the selected device password resumes exact lookup and durable activation without importing again

  @FEAT-009 @AC-009-063 @HV-DAT-RESUME-AC063 @HV-RESUME-LOOKUP @HV-DAT-PROTECTION-MIGRATED
  Scenario: AC-009-063 — HV-DAT-RESUME
    Given Alice restarts an imported protected stage with unavailable connectivity and an owned fault snapshot
    When resumption encounters offline lookup corrupted ciphertext unsupported version and a changed node key
    Then only the repaired exact stage and fresh online verification complete credential activation
