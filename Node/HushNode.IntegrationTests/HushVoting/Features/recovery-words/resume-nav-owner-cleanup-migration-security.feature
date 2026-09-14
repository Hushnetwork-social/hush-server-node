@HushVoting @HV-RECOVERY-WORDS @HV-FEAT-008
@FEAT-008
Feature: Recovery words — resume-nav-owner-cleanup-migration-security
  Covers HV-RW-RESUME, HV-RW-NAV, HV-RW-OWNER, HV-RW-CLEANUP, HV-RW-MIGRATION, HV-RW-SECURITY.

  @FEAT-008 @AC-008-039 @HV-RW-SECURITY-001 @HV-RW-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: AC-008-039 — HV-RW-SECURITY
    Given Alice has registered her identity and removed its local vault
    When Alice restores her recovery words against the live node
    Then the only matching blockchain profile requires confirmation before protection
    And device protection restores the same identity through fresh online verification
    And independent inspection of every retained recovery vault slot finds only the selected concrete keys

  @FEAT-008 @AC-008-052 @HV-RW-SECURITY-002 @HV-PROTECTION-METADATA @HV-RW-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: AC-008-052 — HV-RW-SECURITY
    Given Alice has restored and locked a real password-protected recovery vault
    When closed recovery metadata encounters unknown downgraded native unwrapped and future-version faults
    Then only the unchanged password-protected record and fresh exact verification restore access


  @FEAT-008 @AC-008-056 @HV-RW-RESUME-001 @HV-RW-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: AC-008-056 — HV-RW-RESUME
    Given Alice has confirmed her registered recovery profile and reached device protection
    When the node becomes unavailable after recovery review and Alice stages protection
    Then the staged recovery stays unauthenticated and only its bounded locked preview survives restart
    And the staged recovered identity activates only after connectivity returns

  @FEAT-008 @AC-008-062 @HV-RW-RESUME-002 @HV-STAGED-RESUME @HV-E2E @E2E
  Scenario: AC-008-062 — HV-RW-RESUME
    Given Alice has genuinely staged recovery words in a browser that will lose its process
    When the browser restarts with only its persistent staged vault and no onboarding authority
    Then the required staged-resume heading appears before password unlock and exact online activation

  @FEAT-008 @AC-008-063 @HV-RW-RESUME-003 @HV-RESUME-LOOKUP @HV-RW-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: AC-008-063 — HV-RW-RESUME
    Given Alice has protected recovered keys awaiting final verification in a restartable browser
    When the browser process crashes and recovery resumes from its encrypted storage
    Then resumed recovery looks up the original public keys first and activates without reconstructing words

  @FEAT-008 @AC-008-064 @HV-RW-NAV-001 @HV-RW-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: AC-008-064 — HV-RW-NAV
    Given Alice has entered recovery words before verification with observed input custody
    When Alice uses root Back before verification and after both candidates are resolved
    Then Back after protected recovery locks and preserves the same keys for real online activation

  @FEAT-008 @AC-008-065 @HV-RW-NAV-002 @HV-RW-NAVIGATION @HV-E2E @E2E
  Scenario: AC-008-065 — HV-RW-NAV
    # Web scope per migration decision. Android Back remains FEAT-008 Phase 6 Tasks 6.5/6.6 qualification.
    Given Alice exercises Web recovery Back from entered words with opaque history
    When browser and in-app Back discard recovery review and reject stale or forged history
    And real worker contention keeps recovery Back blocked until cleanup is acknowledged on Retry
    Then both Web Back controls preserve staged keys behind inspection until fresh exact online unlock

  @FEAT-008 @AC-008-066 @HV-RW-OWNER-001 @HV-E2E @E2E
  Scenario: AC-008-066 — HV-RW-OWNER
    Given one live recovery owner
    When another tab attempts recovery
    Then the non-owner is blocked with a safe notification and no secret data is broadcast

  @FEAT-008 @AC-008-067 @HV-RW-OWNER-002 @HV-E2E @E2E
  Scenario: AC-008-067 — HV-RW-OWNER
    Given one live recovery owner
    When another tab attempts recovery
    Then the non-owner is blocked with a safe notification and no secret data is broadcast

  @FEAT-008 @AC-008-068 @HV-RW-CLEANUP-001 @HV-RW-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: AC-008-068 — HV-RW-CLEANUP
    Given Alice has a restored and locked identity with a real vault and owned journal residue
    When confirmed removal waits for the real final storage absence acknowledgement
    Then verified empty custody restores first-run while the same blockchain identity remains


  @FEAT-008 @AC-008-069 @HV-RW-CLEANUP-002 @HV-E2E @E2E
  Scenario: AC-008-069 — HV-RW-CLEANUP
    Given a completed local removal
    When cleanup verification runs
    Then every managed artifact is removed and failure quarantines recovery

  @FEAT-008 @AC-008-070 @HV-RW-CLEANUP-003 @HV-RW-CLEANUP-QUARANTINE @HV-E2E @E2E
  Scenario: AC-008-070 — HV-RW-CLEANUP
    Given Alice has restored her identity and locked its vault before confirmed cleanup
    When a real managed-journal deletion aborts during removal and Alice retries inspection
    Then recovery stays quarantined until Alice freshly confirms removal and all managed custody is verified absent

  @FEAT-008 @AC-008-071 @HV-RW-SECURITY-003 @HV-RW-SUCCESS @HV-E2E @E2E
  Scenario: AC-008-071 — HV-RW-SECURITY
    Given Alice has reviewed her registered recovery identity and the root announcement is observed
    When separate device protection waits for actual identity verification and indexed licence access
    Then recovery announces success once and enters the dashboard without another Continue action
    And ordinary Lock and unlock do not replay the recovery announcement

  @FEAT-008 @AC-008-072 @HV-RW-MIGRATION-001 @HV-E2E @E2E
  Scenario: AC-008-072 — HV-RW-MIGRATION
    # FEAT-036 owns the supported historical Web fixtures and migration contract.
    Given an approved historical Web vault contains ordinary keys and a separate encrypted mnemonic record
    When its owner unlocks through the exact historical protection and migrates to the current contract
    Then the target preserves the concrete keys without loading displaying or retaining the mnemonic record
    And current protection integrity network and exact online identity are verified before authentication
    And the old encrypted generation is retained only for bounded rollback until current version verification succeeds
    And obsolete ciphertext deletion is verified or migration remains quarantined for Retry

  @FEAT-008 @AC-008-073 @HV-RW-SECURITY-004 @HV-E2E @E2E
  Scenario: AC-008-073 — HV-RW-SECURITY
    Given secret-bearing recovery material
    When evidence and artifact scanning runs
    Then trace/screenshot/video are disabled and no prohibited credential material is found

  @FEAT-008 @AC-008-074 @HV-RW-SECURITY-005 @HV-E2E @E2E
  Scenario: AC-008-074 — HV-RW-SECURITY
    Given secret-bearing recovery material
    When evidence and artifact scanning runs
    Then trace/screenshot/video are disabled and no prohibited credential material is found

  @FEAT-008 @AC-008-075 @HV-RW-SECURITY-006 @HV-E2E @E2E
  Scenario: AC-008-075 — HV-RW-SECURITY
    Given secret-bearing recovery material
    When evidence and artifact scanning runs
    Then trace/screenshot/video are disabled and no prohibited credential material is found

  @FEAT-008 @AC-008-076 @HV-RW-SECURITY-007 @HV-SERVER-TWIN @NON_E2E @HV-ORIGINAL-IDENTITY-TWIN
  Scenario: AC-008-076 — HV-RW-SECURITY
    Given the owned server has verified P01 rejection admission exact lookup and same-key reset
    When the owned server verifies P02 admission and resets that indexed chain
    Then forged recreation is rejected and one fresh same-key transaction restores the exact profile

  @FEAT-008 @AC-008-077 @HV-RW-SECURITY-008 @HV-ARTIFACT-PRIVACY @HV-RW-LIFECYCLE-MIGRATED @HV-E2E @E2E
  Scenario: AC-008-077 — HV-RW-SECURITY
    Given Alice arms artifact checks before entering recovery words
    When Alice completes actual recovery and device protection against the node
    Then the recovery journey has disabled captures and clean completed artifact evidence


  @FEAT-008 @AC-008-078 @HV-RW-SECURITY-009 @HV-E2E @E2E
  Scenario: AC-008-078 — HV-RW-SECURITY
    Given secret-bearing recovery material
    When evidence and artifact scanning runs
    Then trace/screenshot/video are disabled and no prohibited credential material is found

  @FEAT-008 @AC-008-079 @HV-RW-SECURITY-010 @HV-ACCESSIBILITY @HV-E2E @E2E
  Scenario: AC-008-079 — HV-RW-SECURITY
    # Automated Web checkpoint evidence; full keyboard/zoom/focus/screen-reader qualification remains pending.
    Given Alice checks Web accessibility across real recovery words and server activation
    Then the measured Web checkpoints have no automated accessibility or layout findings

  @FEAT-008 @AC-008-080 @HV-RW-SECURITY-011 @HV-E2E @E2E
  Scenario: AC-008-080 — HV-RW-SECURITY
    # The original TS steps incorrectly checked artifact policy for this performance AC.
    # Full performance qualification remains pending; partial receipts are mapped in AUD-112.
    Given the approved minimum Web hardware browser and recovery workload are pinned
    When all applicable recovery derivations lookups provisioning and cleanup are measured
    Then ordinary recovery UI meets the 100 millisecond target and progress waits 150 milliseconds
    And complete local derivation meets the one second target without partial candidates or concurrent authority operations
    And recovery enforces ten second RPC and ten minute foreground bounds with one second cleanup acknowledgement
    And device password resource bounds and three second polling with three minute delay handling hold without cryptographic downgrade plaintext persistence or offline authentication

  @FEAT-008 @AC-008-082 @HV-RW-MIGRATION-002 @HV-E2E @E2E
  Scenario: AC-008-082 — HV-RW-MIGRATION
    # Aggregate replacement-contract qualification; an individual conversion is insufficient.
    Given the approved FEAT-003 through FEAT-011 no-mnemonic and optional-protection version matrix
    When every supported contract replacement and migration is executed against its qualified adapter
    Then each incompatible change has a distinct version and only approved historical migrations remain readable
    And creation restore unlock reveal export and protection behavior obey the qualified replacement contracts before release

  @FEAT-008 @AC-008-083 @HV-RW-SECURITY-012 @HV-E2E @E2E @HV-BACKEND-RELEASE
  Scenario: AC-008-083 — HV-RW-SECURITY
    Given the complete HushServerNode signature binding admission status and TwinTest release matrix has verified current-build evidence
    When the real Web missing-profile flow submits its exact signed identity and waits for indexed confirmation
    Then the complete backend prerequisite and this indexed Web journey share the executed build without approving other release gates

  @FEAT-008 @AC-008-084 @HV-RW-SECURITY-013 @HV-E2E @E2E @HV-RELEASE-PINS
  Scenario: AC-008-084 — HV-RW-SECURITY
    # Input integrity is executable; full release/handoff qualification remains separately evaluated.
    Given Alice completes the real recovery Web journey for input pin evidence
    When the exact running build and public contract inputs are recorded by content digest
    Then the recorded pins match the executed sources builds public corpora dependencies and Web handoff definitions
    And input pin integrity does not promote missing qualifications or assert release readiness

  @FEAT-008 @AC-008-085 @HV-RW-SECURITY-014 @HV-EXTERNAL-QUALIFICATION @HV-E2E @E2E
  Scenario: AC-008-085 — HV-RW-SECURITY
    Given the recovery feature requires an independent security review
    When the external review findings and their resolution are evaluated
    Then no relevant High or Critical finding may remain unresolved
    And automated Web tests and code reviews do not satisfy independent qualification
