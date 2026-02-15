@E2E @OwnMessageUnread
Feature: Own messages should not be counted as unread
    As a user
    I want my own sent messages to not appear as unread
    So that the unread badge only reflects messages from others

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    @OwnMsg-E2E-001
    Scenario: User sends message to personal feed and no unread badge appears
        # Setup: Launch browser, create identity (also creates personal feed)
        Given a browser is launched
        And the user has created identity "Alice" via browser
        # Send a message to the personal feed (clicks personal feed, sends, waits for block confirmation)
        And the user has sent message "Hello from Alice" to their personal feed
        # Trigger sync to pick up the new block
        And Alice triggers feed sync
        # Verify: no unread badge should appear on the personal feed
        Then the personal feed should NOT show unread badge
