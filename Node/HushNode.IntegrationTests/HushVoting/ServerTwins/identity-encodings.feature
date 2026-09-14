@HushVoting @HV-SERVER-TWIN @NON_E2E @HV-ID-ENCODING-TWIN @FEAT-011
Feature: HushVoting exact identity encodings and lookup bounds
  FEAT-001 exact address contract; FEAT-011 Phase 3 Tasks 3.1/3.2 and 3.7/3.8.
  Supporting FEAT-008/009 backend evidence, not EPIC browser acceptance.

  @HV-TWIN-ID-ENCODING-001 @AC-008-076 @AC-009-081
  Scenario: P01 compact admission preserves exact encoded identity
    Given an unregistered P01 identity uses an approved compact signature
    When the encoded identity is admitted and its exact bytes are retried before indexing
    Then real indexing preserves the exact encoded pair and a duplicate adds no profile or transaction

  @HV-TWIN-ID-ENCODING-002 @AC-008-076 @AC-009-081
  Scenario: P01 DER admission preserves exact encoded identity
    Given an unregistered P01 identity uses an approved DER signature
    When the encoded identity is admitted and its exact bytes are retried before indexing
    Then real indexing preserves the exact encoded pair and a duplicate adds no profile or transaction

  @HV-TWIN-ID-ENCODING-003 @AC-008-076 @AC-009-081
  Scenario: P02 compact admission preserves exact encoded identity
    Given an unregistered P02 identity uses an approved compact signature
    When the encoded identity is admitted and its exact bytes are retried before indexing
    Then real indexing preserves the exact encoded pair and a duplicate adds no profile or transaction

  @HV-TWIN-ID-ENCODING-004 @AC-008-076 @AC-009-081
  Scenario: P02 DER admission preserves exact encoded identity
    Given an unregistered P02 identity uses an approved DER signature
    When the encoded identity is admitted and its exact bytes are retried before indexing
    Then real indexing preserves the exact encoded pair and a duplicate adds no profile or transaction

  @HV-TWIN-ID-ENCODING-005 @AC-008-076 @AC-009-081
  Scenario: Two encodings of the same curve points remain distinct exact profiles
    Given an unregistered P01 identity uses an approved compact signature
    When the encoded identity is admitted and its exact bytes are retried before indexing
    Then the uncompressed encoding of the same P01 pair remains a separately registered exact identity

  @HV-TWIN-ID-ENCODING-006 @AC-008-076 @AC-009-081
  Scenario: Invalid lookup bounds never produce authoritative absence
    Given an unregistered P01 identity uses an approved compact signature
    When invalid lookup bounds are rejected over RPC while a valid absent identity stays absent
    Then real indexing preserves the exact encoded pair and a duplicate adds no profile or transaction
