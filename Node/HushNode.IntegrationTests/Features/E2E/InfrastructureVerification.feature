@E2E @Infrastructure
Feature: E2E Infrastructure Verification
    As a developer
    I want to verify that the E2E test infrastructure works end-to-end
    So that I can build reliable E2E tests

    Background:
        Given a HushServerNode is running

    Scenario: Browser can connect to HushWebClient container
        Given HushWebClient is running in Docker
        And a browser page is created
        When the browser navigates to the web client
        Then the page should load successfully
        And the page title should contain "Hush"
