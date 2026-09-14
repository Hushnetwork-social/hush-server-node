@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-BINDING-WAIT-TWIN @FEAT-011
Feature: HushVoting signed key-pair binding and delayed identity confirmation
  FEAT-007's named backend blocker boundaries, under FEAT-011 Phase 3 Tasks 3.1–3.8.
  These backend Twins support FEAT evidence; the client owns the three-minute UI warning.

  @HV-TWIN-ID-BINDING-001 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real ingress rejects a changed encryption address despite its valid curve-point encoding
    Given the isolated real node has an unregistered P01 identity and a altered-encryption-binding request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_SIGNATURE and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-WAIT-001 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Three minutes of real mempool waiting never confirms or duplicates a pending identity
    Given one unregistered identity has exact retransmissions for real admission
    When the real mempool retains that exact identity through three minutes without block production
    Then one indexed profile retains the winning exact pair and later retries add no mempool entries
