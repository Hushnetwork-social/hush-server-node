@HushVoting @HV-CREDENTIAL-FILE-RESTORE @HV-FEAT-009
@FEAT-009
Feature: Credential file restore — Navigation, ownership, cleanup, and security
  Covers HV-DAT-NAV, HV-DAT-OWNER, HV-DAT-CLEANUP, HV-DAT-EXTERNAL, HV-DAT-SECURITY.

  @FEAT-009 @AC-009-064 @HV-DAT-NAV-AC064 @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-064 — HV-DAT-NAV
    Given Alice begins restoring a registered backup while success announcements are observed
    When selection and decryption succeed but the restored stage cannot verify online
    Then no intermediate restore phase announces completed restoration or enters the dashboard

  @FEAT-009 @AC-009-065 @HV-DAT-NAV-AC065 @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-065 — HV-DAT-NAV
    Given Alice begins restoring a registered backup while success announcements are observed
    When the restored identity and its root licence complete exact live verification
    Then restoration is announced once and the authenticated dashboard opens automatically

  @FEAT-009 @AC-009-066 @HV-DAT-NAV-AC066 @HV-DAT-BACK @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-066 — HV-DAT-NAV
    Given the credential picker has a real bounded backup available
    When Alice leaves the backup password screen through browser Back
    Then the three-choice root returns and reopening restore has no previous file or password

  @FEAT-009 @AC-009-067 @HV-DAT-NAV-AC067 @HV-DAT-BACK @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-067 — HV-DAT-NAV
    Given Alice has decrypted her registered backup and reached separate device protection
    When Alice goes Back after credential validation but before device staging
    Then empty credential selection returns and only a newly selected backup can complete live restoration

  @FEAT-009 @AC-009-068 @HV-DAT-NAV-AC068 @HV-DAT-BACK @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-068 — HV-DAT-NAV
    Given Alice has decrypted her registered backup and reached separate device protection
    When Alice uses browser Back while the real post-staging identity lookup is still pending
    Then the staged vault stays locked through history navigation and cannot reopen file import

  @FEAT-009 @AC-009-069 @HV-DAT-NAV-AC069 @HV-E2E @E2E
  Scenario: AC-009-069 — HV-DAT-NAV
    Given a navigation event occurs
    When the shared Back authority evaluates the stage
    Then pre-decryption clears, post-validation destroys, and post-stage locks with visible URL remaining root

  @FEAT-009 @AC-009-070 @HV-DAT-OWNER-AC070 @HV-E2E @E2E
  Scenario: AC-009-070 — HV-DAT-OWNER
    Given two authorities attempt restore
    When ownership is acquired atomically
    Then exactly one owner may select, decrypt, stage, or submit and non-owners receive only safe blocked state

  @FEAT-009 @AC-009-071 @HV-DAT-CLEANUP-AC071 @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-071 — HV-DAT-CLEANUP
    Given Alice selects a real unchanged source file containing her encrypted registered keys and recovery words
    When Alice decrypts the backup with its exact untrimmed UTF-8 password
    Then the browser decrypts the approved PBKDF2 and AES-GCM envelope without altering its keys
    And separate device protection restores that identity through the live node
    When Alice locks and removes the restored local identity while keeping her original backup
    Then local import data is absent after restart and the external backup is still unchanged

  @FEAT-009 @AC-009-072 @HV-DAT-CLEANUP-AC072 @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-072 — HV-DAT-CLEANUP
    Given Alice restores a real registered backup while worker cleanup can encounter actual contention
    When real worker contention rejects source cleanup and then validated-candidate cleanup
    Then only acknowledged cleanup permits fresh selection and the unchanged source restores through the live node
    When real worker contention rejects removal of Alice's restored local identity
    Then removal failure preserves the vault and only a fresh confirmed removal returns to verified empty custody

  @FEAT-009 @AC-009-073 @HV-DAT-EXTERNAL-AC073 @HV-DAT-PUBLIC-CONFORMANCE @HV-E2E @E2E
  Scenario: AC-009-073 — HV-DAT-EXTERNAL
    Given both real compatibility runtimes execute the complete public v1 corpus with identical expected outcomes
    When Alice imports the unchanged public positive vector through the real browser picker and live identity lookup
    Then the same public-vector keys gain access only after separate device protection and real licence indexing

  @FEAT-009 @AC-009-074 @HV-DAT-EXTERNAL-AC074 @HV-GENERATED-CORPUS @HV-E2E @E2E
  Scenario: AC-009-074 — HV-DAT-EXTERNAL
    Given the Web corpus uses generated test fixtures unless explicit local inputs are supplied
    When every selected controlled source completes the real Web import and owned-node activation
    Then the controlled corpus has complete passing aggregate evidence and every source remains unchanged

  @FEAT-009 @AC-009-075 @HV-DAT-EXTERNAL-AC075 @HV-GENERATED-CORPUS @HV-E2E @E2E
  Scenario: AC-009-075 — HV-DAT-EXTERNAL
    Given the Web corpus uses generated test fixtures unless explicit local inputs are supplied
    When every selected controlled source completes the real Web import and owned-node activation
    Then the controlled corpus has complete passing aggregate evidence and every source remains unchanged

  @FEAT-009 @AC-009-076 @HV-DAT-EXTERNAL-AC076 @HV-GENERATED-CORPUS @HV-E2E @E2E
  Scenario: AC-009-076 — HV-DAT-EXTERNAL
    Given the Web corpus uses generated test fixtures unless explicit local inputs are supplied
    When an unowned fixture is refused before source opening and the owned fixture completes controlled import
    Then the controlled corpus has complete passing aggregate evidence and every source remains unchanged

  @FEAT-009 @AC-009-077 @HV-DAT-EXTERNAL-AC077 @HV-GENERATED-CORPUS @HV-E2E @E2E
  Scenario: AC-009-077 — HV-DAT-EXTERNAL
    Given the Web corpus uses generated test fixtures unless explicit local inputs are supplied
    When every selected controlled source completes the real Web import and owned-node activation
    Then the controlled corpus has complete passing aggregate evidence and every source remains unchanged

  @FEAT-009 @AC-009-078 @HV-DAT-SECURITY-AC078 @HV-E2E @E2E
  Scenario: AC-009-078 — HV-DAT-SECURITY
    Given secret-bearing scenarios are configured
    When capture policy and scanners run
    Then trace, screenshot, and video are disabled before source or password entry and artifact scans find no prohibited material

  @FEAT-009 @AC-009-079 @HV-DAT-SECURITY-AC079 @HV-E2E @E2E
  Scenario: AC-009-079 — HV-DAT-SECURITY
    Given secret-bearing scenarios are configured
    When capture policy and scanners run
    Then trace, screenshot, and video are disabled before source or password entry and artifact scans find no prohibited material

  @FEAT-009 @AC-009-080 @HV-DAT-SECURITY-AC080 @HV-E2E @E2E
  Scenario: AC-009-080 — HV-DAT-SECURITY
    Given secret-bearing scenarios are configured
    When capture policy and scanners run
    Then trace, screenshot, and video are disabled before source or password entry and artifact scans find no prohibited material

  @FEAT-009 @AC-009-081 @HV-DAT-SECURITY-AC081 @HV-SERVER-TWIN @NON_E2E @HV-ORIGINAL-IDENTITY-TWIN
  Scenario: AC-009-081 — HV-DAT-SECURITY
    Given the owned server has verified P01 rejection admission exact lookup and same-key reset
    When the owned server verifies P02 admission and resets that indexed chain
    Then forged recreation is rejected and one fresh same-key transaction restores the exact profile

  @FEAT-009 @AC-009-082 @HV-DAT-SECURITY-AC082 @HV-ARTIFACT-PRIVACY @HV-DAT-NAVIGATION-MIGRATED @HV-E2E @E2E
  Scenario: AC-009-082 — HV-DAT-SECURITY
    Given Alice arms artifact checks before selecting her credential source
    When Alice completes actual file import and separate device protection against the node
    Then the file journey has disabled captures and clean completed artifact evidence


  @FEAT-009 @AC-009-083 @HV-DAT-SECURITY-AC083 @HV-E2E @E2E
  Scenario: AC-009-083 — HV-DAT-SECURITY
    Given secret-bearing scenarios are configured
    When capture policy and scanners run
    Then trace, screenshot, and video are disabled before source or password entry and artifact scans find no prohibited material

  @FEAT-009 @AC-009-084 @HV-DAT-SECURITY-AC084 @HV-ACCESSIBILITY @HV-E2E @E2E
  Scenario: AC-009-084 — HV-DAT-SECURITY
    # Automated Web checkpoint evidence; full keyboard/zoom/focus/screen-reader qualification remains pending.
    Given Alice checks Web accessibility across real credential import and server activation
    Then the measured Web checkpoints have no automated accessibility or layout findings

  @FEAT-009 @AC-009-085 @HV-DAT-SECURITY-AC085 @HV-E2E @E2E
  Scenario: AC-009-085 — HV-DAT-SECURITY
    # The original TS steps incorrectly checked artifact policy for this performance AC.
    # Full performance qualification remains pending; partial receipts are mapped in AUD-112.
    Given the approved minimum Web hardware browser and credential import workload are pinned
    When credential reads KDF decryption strict parsing local proof lookup and cleanup are measured
    Then ordinary import UI meets the 100 millisecond target and progress waits 150 milliseconds
    And reads allocate at most one MiB plus one overflow byte and cancel after thirty seconds of inactivity
    And backup passwords are bounded to 4096 bytes and PBKDF2 retains exactly 100000 iterations
    And post PBKDF2 processing meets the one second target with ten second RPC ten minute foreground and one second cleanup bounds
    And three second polling and three minute delay handling never permit caching partial parsing weaker cryptography plaintext fallback or offline authentication

  @FEAT-009 @AC-009-086 @HV-DAT-SECURITY-AC086 @HV-E2E @E2E
  Scenario: AC-009-086 — HV-DAT-SECURITY
    # FEAT-036: this is the inherited replacement-contract gate, not artifact scanning.
    Given credential import consumes the approved FEAT-008 replacement-contract and migration matrix
    When no-mnemonic optional-protection and historical compatibility qualifications are evaluated for release
    Then the exact versioned replacements and supported migrations are implemented and qualified
    And an artifact scan or one successful current-format import cannot substitute for that contract evidence

  @FEAT-009 @AC-009-087 @HV-DAT-SECURITY-AC087 @HV-E2E @E2E @HV-BACKEND-RELEASE
  Scenario: AC-009-087 — HV-DAT-SECURITY
    Given the complete HushServerNode signature binding admission status and TwinTest release matrix has verified current-build evidence
    When the real Web missing-profile flow submits its exact signed identity and waits for indexed confirmation
    Then the complete backend prerequisite and this indexed Web journey share the executed build without approving other release gates

  @FEAT-009 @AC-009-088 @HV-DAT-SECURITY-AC088 @HV-E2E @E2E @HV-RELEASE-PINS
  Scenario: AC-009-088 — HV-DAT-SECURITY
    # Input integrity is executable; full release/handoff qualification remains separately evaluated.
    Given Alice completes the real import Web journey for input pin evidence
    When the exact running build and public contract inputs are recorded by content digest
    Then the recorded pins match the executed sources builds public corpora dependencies and Web handoff definitions
    And input pin integrity does not promote missing qualifications or assert release readiness

  @FEAT-009 @AC-009-089 @HV-DAT-SECURITY-AC089 @HV-EXTERNAL-QUALIFICATION @HV-E2E @E2E
  Scenario: AC-009-089 — HV-DAT-SECURITY
    Given credential-file restoration requires an independent security review
    When the external review findings and their resolution are evaluated
    Then no relevant High or Critical finding may remain unresolved
    And automated Web tests and code reviews do not satisfy independent qualification
