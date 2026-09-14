@HushVoting @HV-E2E @E2E @HV-CORPUS-HARNESS @HV-FEAT-009
Feature: Public diagnostics for the controlled corpus Web harness
  These synthetic self-tests are not original controlled-corpus acceptance results.

  @HV-CORPUS-CHECK-POSITIVE
  Scenario: Public sources complete actual import with aggregate-only evidence
    Given four synthetic public credential sources exercise both approved producers and mnemonic shapes
    When every selected controlled source completes the real Web import and owned-node activation
    Then all four public sources pass without being reported as controlled-corpus qualification

  @HV-CORPUS-CHECK-CHANGE
  Scenario: Actual source modification is detected after Web import
    Given four synthetic public credential sources exercise both approved producers and mnemonic shapes
    When only the owned public source is changed after its real browser import
    Then the harness detects the changed source without emitting identifying records

  @HV-CORPUS-CHECK-REFUSAL
  Scenario: Unowned and cancelled corpus runs open no sources
    Given four synthetic public credential sources exercise both approved producers and mnemonic shapes
    When an unowned fixture and a cancelled run attempt to open the public corpus
    Then neither attempt opens a source or produces a passing corpus claim
