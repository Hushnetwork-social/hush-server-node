@E2E @EPIC-003 @FEAT-055
Feature: House-cleaning on Feed Close
    As a user navigating between feeds
    I want the previous feed's data to be cleaned up automatically
    So that the app remains performant and memory usage stays controlled

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    @HouseCleaning @Navigation
    Scenario: Navigating to Settings triggers cleanup for current feed
        # Setup: Create user and send some messages
        Given the user has created identity "Alice" via browser
        And Alice has sent 5 messages to their personal feed
        And the localStorage contains messages for the personal feed

        # Action: Navigate away from the feed (to New Feed dialog)
        When Alice clicks on the "new-chat" navigation item

        # Verification: After debounce, cleanup should be triggered
        And Alice waits for cleanup debounce (200ms)
        Then the cleanupFeed function should have been called

    @HouseCleaning @FeedSwitch
    Scenario: Switching between feeds triggers cleanup for previous feed
        # Setup: Create user and send messages to personal feed
        # Note: Without multi-user infrastructure, we simulate feed switching by
        # navigating away and back, which triggers the same cleanup mechanism
        Given the user has created identity "Alice" via browser
        And Alice has sent 3 messages to their personal feed
        And the localStorage contains messages for the personal feed

        # Action: Navigate away (triggers cleanup) then navigate back
        When Alice clicks on the "new-chat" navigation item

        # Verification: After debounce, cleanup should be triggered
        And Alice waits for cleanup debounce (200ms)
        Then the cleanupFeed function should have been called

    @HouseCleaning @LocalStorage @Slow
    Scenario: Cache is trimmed to limit on feed close
        # Note: This test requires server-side message seeding for full validation
        # With current infrastructure, we verify the mechanism with fewer messages
        Given the user has created identity "Alice" via browser
        And Alice has sent 5 messages to their personal feed
        And the messages are stored in localStorage

        # Action: Navigate away (triggers cleanup) and back
        When Alice clicks on the "new-chat" navigation item
        And Alice waits for cleanup debounce (200ms)
        And Alice clicks on the "feeds" navigation item

        # Verification: Messages should still be accessible (not yet over limit)
        Then the personal feed should load successfully
        And the messages should be visible

    @HouseCleaning @ReturnToFeed
    Scenario: Returning to feed shows messages from bottom
        Given the user has created identity "Alice" via browser
        And Alice has sent 5 messages to their personal feed
        And the messages are visible in the chat

        # Action: Navigate away (triggers cleanup) and return
        When Alice clicks on the "new-chat" navigation item
        And Alice waits for cleanup debounce (200ms)
        And Alice clicks on the "feeds" navigation item
        And Alice clicks on their personal feed

        # Verification: Chat should show messages (scroll position at bottom)
        Then the most recent message should be visible
        And the chat view should be at the bottom

    @HouseCleaning @TabClose @Manual
    Scenario: Tab close triggers best-effort cleanup
        # Note: This scenario is difficult to automate reliably
        # The beforeunload cleanup is best-effort and may not complete before tab closes
        # Manual testing recommended for this scenario
        Given the user has created identity "Alice" via browser
        And Alice has sent 3 messages to their personal feed

        # Action: Close the browser tab
        When Alice closes the browser tab

        # Verification: Cleanup was attempted (best-effort)
        # This cannot be fully verified in E2E as the browser is closed
        Then the cleanup should have been attempted before unload
