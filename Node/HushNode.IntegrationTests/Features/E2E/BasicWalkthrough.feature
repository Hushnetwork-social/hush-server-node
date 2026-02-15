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
        # Step 1: Navigate to auth page
        When the user navigates to "/auth"

        # Step 2: Create identity (submits identity transaction to mempool)
        And the user creates a new identity with display name "TestUser"

        # Step 3: Wait for identity transaction to be mined into a block
        And the identity transaction is processed

        # Step 4: Client detects identity confirmed, auto-submits personal feed transaction
        # Wait for personal feed transaction to be mined into a block
        And the personal feed transaction is processed

        # Step 5: Client redirects to dashboard after identity is confirmed
        Then the user should be redirected to "/dashboard"

        # Step 6: Dashboard syncs feeds and renders the personal feed
        And the feed list should show the personal feed for "TestUser"

    @PersonalFeed
    Scenario: User sends message to personal feed
        # Setup: Create identity first (uses event-based waiting)
        Given the user has created identity "TestUser" via browser

        # Action: Send message
        When the user clicks on their personal feed
        And the user sends message "Hello World!"
        # Wait for message transaction to reach mempool, produce block, wait for indexing
        And the transaction is processed

        # Verification
        Then the message "Hello World!" should be visible in the chat
        And the message should show "TestUser" as the sender
        And the message "Hello World!" should show confirmed status icon

    @Reactions
    Scenario: User adds reaction to a message
        # Setup: Create identity and send message
        Given the user has created identity "TestUser" via browser
        And the user has sent message "Test message" to their personal feed

        # Action: Add reaction (using thumbs-up emoji index 0)
        When the user adds reaction 0 to the message "Test message"
        # Wait for reaction transaction to reach mempool, produce block, wait for indexing
        And the transaction is processed

        # Verification
        Then the message "Test message" should show a reaction badge
