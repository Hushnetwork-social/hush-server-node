@E2E @BasicWalkthrough @PR @Critical
Feature: Basic User Walkthrough
    As a new user
    I want to create my identity, send a message, and add a reaction
    So that I can verify the full system works end-to-end

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    @Identity
    Scenario: New user creates identity through browser
        When the user navigates to "/auth"
        And the user creates a new identity with display name "TestUser"
        # The web client handles waiting for blockchain transactions internally
        # The dashboard redirect confirms the identity was created successfully
        Then the user should be redirected to "/dashboard"

    @PersonalFeed
    Scenario: User sends message to personal feed
        # Setup: Create identity first
        Given the user has created identity "TestUser" via browser
        And a block is produced

        # Action: Send message
        When the user clicks on their personal feed
        And the user sends message "Hello World!"
        And a block is produced

        # Verification
        Then the message "Hello World!" should be visible in the chat
        And the message should show "TestUser" as the sender

    @Reactions
    Scenario: User adds reaction to a message
        # Setup: Create identity and send message
        Given the user has created identity "TestUser" via browser
        And a block is produced
        And the user has sent message "Test message" to their personal feed
        And a block is produced

        # Action: Add reaction (using thumbs-up emoji index 0)
        When the user adds reaction 0 to the message "Test message"
        And a block is produced

        # Verification
        Then the message "Test message" should show a reaction badge
