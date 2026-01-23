@E2E @GroupJoin @PR
Feature: Group Join Basic Walkthrough
    As a user
    I want to create a public group and view the invite link
    So that I can invite others to join my group

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    @GroupCreation
    Scenario: User creates a public group
        # Setup: Create identity first
        Given the user has created identity "GroupCreator" via browser

        # Step 1: Navigate to dashboard and open group creation wizard
        When the user clicks the "Create Group" navigation button
        Then the group creation wizard should be visible

        # Step 2: Select public group type
        When the user selects "public" group type
        And the user clicks the type selection next button

        # Step 3: Fill group details and create
        And the user fills group name "Test Group"
        And the user fills group description "A test group for E2E testing"
        And the user clicks confirm create group button
        # Wait for group creation transaction to be processed
        And the transaction is processed

        # Step 4: Verify group appears in feed list
        Then the group "Test Group" should appear in the feed list

    @InviteLink
    Scenario: User views invite link for public group
        # Setup: Create identity and group
        Given the user has created identity "GroupCreator" via browser
        And the user has created a public group "Team Alpha" via browser

        # Action: View invite link in group settings
        When the user opens the group "Team Alpha"
        And the user opens group settings
        Then the invite link should be visible
        And the invite code should be visible
