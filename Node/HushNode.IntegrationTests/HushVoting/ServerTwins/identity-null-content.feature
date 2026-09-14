@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-NULL-TWIN @FEAT-011
Feature: HushVoting missing and null identity content rejects before admission
  FEAT-011 Phase 2 Tasks 2.5/2.6 and Phase 3 Tasks 3.1/3.2, 3.7/3.8.
  Supports pending FEAT-007/008/009 backend matrices, not EPIC acceptance.

  @HV-TWIN-ID-NULL-001 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects null-payload with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-payload request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_MALFORMED_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-002 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects missing-payload with its typed result
    Given the isolated real node has an unregistered P01 identity and a missing-payload request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_MALFORMED_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-003 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects null-user-signature with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-user-signature request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_MALFORMED_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-004 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects missing-user-signature with its typed result
    Given the isolated real node has an unregistered P01 identity and a missing-user-signature request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_MALFORMED_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-005 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects null-alias with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-alias request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_ALIAS_OUT_OF_BOUNDS and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-006 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects missing-alias with its typed result
    Given the isolated real node has an unregistered P01 identity and a missing-alias request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_ALIAS_OUT_OF_BOUNDS and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-007 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects null-signing-address with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-signing-address request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_SIGNING_ADDRESS and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-008 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects null-encryption-address with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-encryption-address request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_ENCRYPTION_ADDRESS and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-009 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects null-signature-value with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-signature-value request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_UNSUPPORTED_SIGNATURE_ENCODING and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-010 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects null-signatory with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-signatory request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_TRANSACTION_ID and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-011 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects array-payload with its typed result
    Given the isolated real node has an unregistered P01 identity and a array-payload request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-NULL-012 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real identity ingress rejects array-user-signature with its typed result
    Given the isolated real node has an unregistered P01 identity and a array-user-signature request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair
