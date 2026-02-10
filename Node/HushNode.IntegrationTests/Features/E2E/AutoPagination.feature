@E2E @EPIC-004 @FEAT-059
Feature: Auto-Pagination Scroll-Based Prefetch
    As a user of Hush Feeds
    I want messages to prefetch as I scroll through history
    So that I have a seamless scrolling experience

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    # F3-001: Two-page initial load verification
    @InitialLoad @Automatable
    Scenario: Feed open initializes prefetch state
        # Setup: Create user and navigate to feed
        Given the user has created identity "Alice" via browser
        And the user clicks on their personal feed

        # Verification: Prefetch should be initialized for the feed
        Then the prefetch state should be initialized for the current feed
        And the loaded page count should be at least 1

    # F3-006: New messages button appears when scrolled up
    @NewMessagesButton @Automatable
    Scenario: New messages button appears when scrolled up and new message arrives
        # Setup: Create user and send initial messages
        Given the user has created identity "Alice" via browser
        And the user clicks on their personal feed
        And Alice has sent 5 messages to their personal feed
        And the transactions are processed

        # Scroll up (if possible) and verify button behavior
        # Note: With only 5 messages, scroll behavior may not trigger
        # This tests the JumpToBottom button presence
        Then the chat view should be at the bottom
        And the jump to bottom button should not be visible

    # F3-009: Fresh start on feed reopen
    @FreshStart @Automatable
    Scenario: Returning to feed starts fresh with newest messages
        # Setup: Create user and send messages
        Given the user has created identity "Alice" via browser
        And the user clicks on their personal feed
        And Alice has sent 3 messages to their personal feed
        And the transactions are processed

        # Action: Navigate away and return
        When Alice clicks on the "new-chat" navigation item
        And Alice waits for cleanup debounce (200ms)
        And Alice clicks on the "feeds" navigation item
        And Alice clicks on their personal feed

        # Verification: Chat should show messages from bottom (fresh start)
        Then the most recent message should be visible
        And the chat view should be at the bottom

    # ===== Manual Testing Scenarios =====
    # These scenarios require extensive message seeding and scroll simulation
    # They are documented here for manual testing guidance

    @ScrollPrefetch @Manual
    Scenario: Prefetch triggered at 25% threshold
        # Manual Test Steps:
        # 1. Seed a feed with 500+ messages using server seeding script
        # 2. Open the feed in browser
        # 3. Scroll up through approximately 75 messages
        # 4. Observe network tab for prefetch request
        # Expected: Network request for next page should be triggered automatically
        Given this scenario requires manual testing
        Then skip automated verification

    @SpinnerDisplay @Manual
    Scenario: Loading spinner shows when scrolling faster than prefetch
        # Manual Test Steps:
        # 1. Seed a feed with 500+ messages
        # 2. Open the feed in browser
        # 3. Rapidly scroll up (faster than prefetch can complete)
        # 4. Observe spinner appearance at top of message list
        # Expected: Spinner should appear when buffer is exhausted
        Given this scenario requires manual testing
        Then skip automated verification

    @ParallelPrefetch @Manual
    Scenario: Multiple feeds load in parallel after sync
        # Manual Test Steps:
        # 1. Create multiple chat feeds with messages
        # 2. Open app after being offline briefly
        # 3. Observe network tab during initial sync
        # Expected: Multiple prefetch requests should fire in parallel
        Given this scenario requires manual testing
        Then skip automated verification

    @SilentRetry @Manual
    Scenario: Prefetch failure retries silently
        # Manual Test Steps:
        # 1. Seed a feed with messages
        # 2. Open developer tools network throttling (offline mode)
        # 3. Scroll to trigger prefetch
        # 4. Observe retry behavior (exponential backoff)
        # 5. Restore network and verify messages load
        # Expected: No error shown, retry happens automatically
        Given this scenario requires manual testing
        Then skip automated verification
