@E2E @HushVoting @EPIC-015 @FEAT-136
Feature: HushVoting Receipt Verifier
    As a public HushVoting verifier
    I want to verify a receipt against a finalized package in the browser
    So that receipt inclusion does not depend on the original device or session

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    @HV-E2E-FEAT-136 @PR
    Scenario: Public verifier verifies a package-bound receipt against a finalized package ZIP
        Given the FEAT-136 package-bound receipt and finalized package ZIP are prepared for the browser
        When the public user opens the receipt verifier
        And the public user imports the FEAT-136 receipt and package ZIP
        And the public user runs receipt verification
        Then the FEAT-136 receipt verifier should show a verified included result
        And the FEAT-136 receipt verifier should not show forbidden private voting data
