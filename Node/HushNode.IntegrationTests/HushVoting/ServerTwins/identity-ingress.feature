@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-INGRESS-TWIN @FEAT-011
Feature: HushVoting backend identity ingress rejection and reservation integrity
  FEAT-011 Phase 3 Tasks 3.1/3.2 and 3.7/3.8.
  Supports the pending FEAT-007/008/009 backend matrices; not EPIC acceptance.

  @HV-TWIN-ID-INGRESS-001 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects forged-signature without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a forged-signature request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_SIGNATURE and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-002 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects wrong-signing-key without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a wrong-signing-key request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_SIGNATURE and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-003 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects altered-payload without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a altered-payload request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_SIGNATURE and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-004 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects signatory-mismatch without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a signatory-mismatch request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_SIGNATORY_MISMATCH and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-005 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects malformed-signature without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a malformed-signature request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_UNSUPPORTED_SIGNATURE_ENCODING and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-006 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects payload-size-mismatch without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a payload-size-mismatch request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_PAYLOAD_SIZE and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-007 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects invalid-encryption-encoding without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a invalid-encryption-encoding request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_ENCRYPTION_ADDRESS and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-008 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects unsupported-payload-kind without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a unsupported-payload-kind request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-INGRESS-009 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects malformed-json without retaining an identity
    Given the isolated real node has an unregistered P01 identity and a malformed-json request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair
