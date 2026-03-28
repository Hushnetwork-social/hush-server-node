@E2E @FoundationWalkthrough @PR @Critical
Feature: Foundation User Walkthrough
    As a new user
    I want to create my identity, send a message, and react to another user's message
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

        # Step 5: Client redirects to feeds after identity is confirmed
        Then the user should be redirected to "/feeds"

        # Step 6: Feeds page syncs feeds and renders the personal feed
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

    @Reactions @ReactionVerifierDev
    Scenario: User adds reaction to another user's message
        # Setup: Alice creates identity in browser, Bob provides a backend chat message
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And the browser forces dev-mode reactions
        And Alice has a backend ChatFeed with "Bob"
        And Bob sends a confirmed backend message "Test message" to ChatFeed(Alice,Bob)

        # Action: Alice syncs, opens the chat, and reacts to Bob's message in dev-mode
        When Alice triggers feed sync
        And Alice opens ChatFeed with "Bob" in browser
        Then Alice should see message "Test message" from Bob
        When the user adds reaction 0 to the message "Test message"
        And the transaction is processed

        # Verification
        Then the message "Test message" should show a reaction badge
