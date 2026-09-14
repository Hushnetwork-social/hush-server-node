@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-FIELD-TWIN @FEAT-011
Feature: HushVoting malformed identity fields reject without reserving keys
  EPIC-001 -> FEAT-011 Phase 3 Tasks 3.1/3.2 and 3.7/3.8.
  Supplemental backend FEAT evidence for AC-007-071/076, AC-008-076/083,
  and AC-009-081/087. These do not replace browser/server EPIC acceptance.

  @HV-TWIN-ID-FIELD-001 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects numeric-alias with its typed result
    Given the isolated real node has an unregistered P01 identity and a numeric-alias request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-002 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects object-alias with its typed result
    Given the isolated real node has an unregistered P01 identity and a object-alias request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-003 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects boolean-signing-address with its typed result
    Given the isolated real node has an unregistered P01 identity and a boolean-signing-address request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-004 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects array-encryption-address with its typed result
    Given the isolated real node has an unregistered P01 identity and a array-encryption-address request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-005 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects string-visibility with its typed result
    Given the isolated real node has an unregistered P01 identity and a string-visibility request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-006 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects numeric-signature with its typed result
    Given the isolated real node has an unregistered P01 identity and a numeric-signature request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-007 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects object-signatory with its typed result
    Given the isolated real node has an unregistered P01 identity and a object-signatory request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-008 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects string-payload-size with its typed result
    Given the isolated real node has an unregistered P01 identity and a string-payload-size request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-009 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects fractional-payload-size with its typed result
    Given the isolated real node has an unregistered P01 identity and a fractional-payload-size request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-010 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects overflow-payload-size with its typed result
    Given the isolated real node has an unregistered P01 identity and a overflow-payload-size request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-011 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects null-payload-size with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-payload-size request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-012 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects negative-payload-size with its typed result
    Given the isolated real node has an unregistered P01 identity and a negative-payload-size request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_PAYLOAD_SIZE and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-013 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects missing-payload-size with its typed result
    Given the isolated real node has an unregistered P01 identity and a missing-payload-size request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_PAYLOAD_SIZE and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-014 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects malformed-transaction-id with its typed result
    Given the isolated real node has an unregistered P01 identity and a malformed-transaction-id request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-015 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects numeric-transaction-id with its typed result
    Given the isolated real node has an unregistered P01 identity and a numeric-transaction-id request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-016 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects empty-transaction-id with its typed result
    Given the isolated real node has an unregistered P01 identity and a empty-transaction-id request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_TRANSACTION_ID and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-017 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects null-transaction-id with its typed result
    Given the isolated real node has an unregistered P01 identity and a null-transaction-id request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_TRANSACTION_ID and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-018 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects missing-transaction-id with its typed result
    Given the isolated real node has an unregistered P01 identity and a missing-transaction-id request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code FULL_IDENTITY_INVALID_TRANSACTION_ID and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-019 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects malformed-timestamp with its typed result
    Given the isolated real node has an unregistered P01 identity and a malformed-timestamp request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-020 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects numeric-timestamp with its typed result
    Given the isolated real node has an unregistered P01 identity and a numeric-timestamp request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-FIELD-021 @AC-007-071 @AC-007-076 @AC-008-076 @AC-008-083 @AC-009-081 @AC-009-087
  Scenario: Real identity ingress rejects object-timestamp with its typed result
    Given the isolated real node has an unregistered P01 identity and a object-timestamp request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair
