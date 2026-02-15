@E2E @IdentityNameChange
Feature: Identity Name Change via Account Details
    As a user
    I want to change my display name through the Account Details page
    So that my personal feed and other users see my updated name

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    @PersonalFeedTitle
    Scenario: User changes display name and personal feed title updates
        # Setup: Create identity
        Given the user has created identity "Alice" via browser

        # Action: Navigate to Account Details and change name
        When the user opens the user menu
        And the user clicks Account Details
        And the user changes display name to "AliceRenamed"
        And the user saves the display name change
        And the name change transaction is processed

        # Verification: Navigate back to dashboard and check updated name
        When the user returns to the dashboard
        Then the personal feed should show name "AliceRenamed"
        And the sidebar should show username "AliceRenamed"
