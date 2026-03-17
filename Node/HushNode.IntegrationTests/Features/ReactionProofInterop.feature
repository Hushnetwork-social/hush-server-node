@Integration @FEAT-087 @HS-INT-087-CROSS-RUNTIME-PROOF
Feature: Reaction proof cross-runtime interop
  As a FEAT-087 maintainer
  I want a minimal cross-runtime proof example
  So that TypeScript-generated Groth16 proofs can be injected directly into the .NET verifier path

  Scenario: TypeScript reaction proof verifies through the .NET verifier DLL
    Given FEAT-087 approved cross-runtime reaction proof artifacts are available
    When TypeScript generates reaction proof fixture "first" for cross-runtime interop
    Then the generated proof payload should be captured as plain JSON for .NET injection
    And the .NET reaction verifier DLL should accept the generated proof
