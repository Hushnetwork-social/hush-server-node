@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-SHAPE-TWIN @FEAT-011
Feature: HushVoting malformed transaction root and payload-kind boundaries
  FEAT-011 Phase 3 Tasks 3.1/3.2 and 3.7/3.8.
  Supports pending FEAT-007/008/009 backend matrices, not EPIC acceptance.

  @HV-TWIN-ID-SHAPE-001 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects missing-payload-kind as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a missing-payload-kind request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-SHAPE-002 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects numeric-payload-kind as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a numeric-payload-kind request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-SHAPE-003 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects object-payload-kind as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a object-payload-kind request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-SHAPE-004 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects array-payload-kind as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a array-payload-kind request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-SHAPE-005 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects array-root as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a array-root request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-SHAPE-006 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects number-root as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a number-root request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-SHAPE-007 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects string-root as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a string-root request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair

  @HV-TWIN-ID-SHAPE-008 @AC-007-071 @AC-008-076 @AC-009-081
  Scenario: Real ingress rejects boolean-root as malformed transaction input
    Given the isolated real node has an unregistered P01 identity and a boolean-root request
    When the defective request reaches the unchanged SubmitSignedTransaction RPC
    Then ingress returns REJECTED with stable code MALFORMED_TRANSACTION_JSON and no mempool or indexed identity effect
    And the original valid identity is still accepted once and indexes with its exact public pair
