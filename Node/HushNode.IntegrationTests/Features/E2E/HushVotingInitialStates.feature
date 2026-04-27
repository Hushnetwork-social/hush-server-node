@E2E @HushVoting @EPIC-013
Feature: HushVoting Initial Empty States
    As an authenticated HushVoting user
    I want the first election screens to load against an empty election database
    So that new users can start an election without query proxy regressions

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched
        And the user has created identity "Election Admin" via browser

    @HV-E2E-EMPTY-INITIAL @PR
    Scenario: Empty election hub and create draft screens load without proxy errors
        When the user opens HushVoting
        Then the HushVoting hub should show the empty linked-election state
        And the HushVoting screen should not show an election query proxy error

        When the user opens Create Election from the HushVoting menu
        Then the HushVoting create-election workspace should show a blank draft
        And the HushVoting screen should not show an election query proxy error
