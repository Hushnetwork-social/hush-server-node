@E2E @EPIC-004 @FEAT-059
Feature: Auto-Pagination Scroll-Based Prefetch
    As a user of Hush Feeds
    I want messages to prefetch as I scroll through history
    So that I have a seamless scrolling experience

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    # F3-001: Prefetch state initialization verification
    @InitialLoad @Automatable
    Scenario: Feed open initializes prefetch state
        # Setup: Create user and navigate to feed
        Given the user has created identity "Alice" via browser
        And the user clicks on their personal feed

        # Verification: Prefetch should be initialized for the feed
        # Note: loadedPageCount is 0 for an empty feed (correct behavior)
        Then the prefetch state should be initialized for the current feed

    # F3-006 & F3-007: Jump to bottom button behavior
    # Verifies button appears when scrolled up, and clicking it jumps to bottom instantly
    @JumpToBottom @Automatable
    Scenario: Jump to bottom button appears when scrolled up and clicking scrolls to bottom
        # Setup: Create user and send enough messages to enable scrolling
        Given the user has created identity "Alice" via browser
        And the user clicks on their personal feed
        And Alice has sent 15 messages to their personal feed
        And the transactions are processed

        # Initially at bottom - button should not be visible
        Then the chat view should be at the bottom
        And the jump to bottom button should not be visible

        # Scroll up to view old messages
        When Alice scrolls up in the message list
        Then the jump to bottom button should be visible

        # Click jump button to return to bottom instantly
        When Alice clicks the jump to bottom button
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

    # F3-008: Keep all pages in memory while in feed
    @KeepInMemory @Manual
    Scenario: All loaded pages remain in memory while viewing feed
        # Manual Test Steps:
        # 1. Seed a feed with 500+ messages
        # 2. Open the feed in browser
        # 3. Scroll through the entire history (load pages 1-5)
        # 4. Open DevTools and check feedsStore state via:
        #    window.__zustand_stores.feedsStore.getState().inMemoryMessages
        # 5. Verify all 500 messages remain in memory
        # 6. Scroll back down to newest messages
        # 7. Verify older messages are still accessible without re-fetching
        # Expected: FEAT-053's 100-message limit is NOT applied while in feed
        #           All pages stay in inMemoryMessages + persisted messages
        Given this scenario requires manual testing
        Then skip automated verification
