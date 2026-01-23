@Integration
Feature: Playwright Infrastructure Verification
    As a developer
    I want to verify that Playwright browser automation is working
    So that I can build E2E tests with confidence

    Background:
        Given the Playwright browser is initialized

    Scenario: Browser can launch and navigate to a page
        When I create a new browser page
        And I navigate to "about:blank"
        Then the page URL should be "about:blank"

    Scenario: Browser contexts are isolated from each other
        When I create two separate browser contexts
        Then the contexts should be different instances

    Scenario: Browser disposes cleanly without errors
        When I dispose the Playwright browser
        Then the browser should no longer be initialized
        And no errors should occur
