@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-ADMISSION-TWIN @FEAT-011
Feature: HushVoting backend concurrent admission and ambiguous transport
  FEAT-011 Phase 2 Tasks 2.7/2.8 and Phase 3 Tasks 3.3–3.8.
  Supports the FEAT-007/008/009 backend matrices; not EPIC acceptance.

  @HV-TWIN-ID-ADMISSION-001 @AC-007-071 @AC-007-072 @AC-008-076 @AC-009-081
  Scenario: Eight real exact retransmissions reserve and index one identity
    Given one unregistered identity has exact retransmissions for real admission
    When every competing RPC reaches a barrier before real admission is released
    Then the node admits exactly one transaction and preserves typed duplicate or conflict outcomes
    And one indexed profile retains the winning exact pair and later retries add no mempool entries

  @HV-TWIN-ID-ADMISSION-002 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Competing same-key profiles retain one winner and a stable conflict
    Given one unregistered identity has two competing signed profiles for real admission
    When every competing RPC reaches a barrier before real admission is released
    Then the node admits exactly one transaction and preserves typed duplicate or conflict outcomes
    And one indexed profile retains the winning exact pair and later retries add no mempool entries

  @HV-TWIN-ID-ADMISSION-003 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: A lost accepted response retries the exact bytes while pending
    Given one unregistered identity has exact retransmissions for real admission
    When an accepted submission response is lost and the exact retry arrives before indexing
    Then one indexed profile retains the winning exact pair and later retries add no mempool entries

  @HV-TWIN-ID-ADMISSION-004 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: A lost accepted response retries the exact bytes after indexing
    Given one unregistered identity has exact retransmissions for real admission
    When an accepted submission response is lost and the exact retry arrives after indexing
    Then one indexed profile retains the winning exact pair and later retries add no mempool entries
