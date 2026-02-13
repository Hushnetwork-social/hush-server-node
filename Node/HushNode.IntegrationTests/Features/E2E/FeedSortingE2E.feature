@E2E @FEAT-062
Feature: FEAT-062 Feed Sorting E2E (blockIndex-based)
    As a user viewing my feed list in the browser
    I want feeds sorted by most recent activity (blockIndex)
    So that my most active conversations appear at the top

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    # Verify feeds are ordered by blockIndex in the browser - most recent on top
    @F2-E2E-001
    Scenario: Feeds ordered by blockIndex - most recent message on top
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        And Alice has a backend ChatFeed with "Charlie"
        When Alice sends a confirmed backend message "Old message" to ChatFeed(Alice,Bob)
        And Alice sends a confirmed backend message "New message" to ChatFeed(Alice,Charlie)
        And Alice triggers feed sync
        Then the feed list should show ChatFeed with "Charlie" above ChatFeed with "Bob"
        And the personal feed should be at position 0

    # Verify personal feed is always pinned at top regardless of chat activity
    @F2-E2E-002
    Scenario: Personal feed always pinned at top regardless of activity
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        When Alice sends a confirmed backend message "Activity!" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        Then the personal feed should be at position 0
        And ChatFeed with "Bob" should be at position 1

    # Verify pending message boosts feed to top and shows pending icon
    @F2-E2E-003
    Scenario: Pending message boosts feed to top and shows pending icon
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        And Alice has a backend ChatFeed with "Charlie"
        And Alice sends a confirmed backend message "Old" to ChatFeed(Alice,Bob)
        And Alice sends a confirmed backend message "Older" to ChatFeed(Alice,Charlie)
        And Alice triggers feed sync
        When Alice opens ChatFeed with "Charlie" in browser
        And Alice sends a pending message "Unconfirmed!" via browser
        Then the message should show pending icon
        When Alice navigates back to feed list
        Then ChatFeed with "Charlie" should be at position 1
        When a block is produced
        And Alice triggers feed sync
        Then the message should show confirmed icon after sync

    # Verify last feed with pending message is on top
    @F2-E2E-004
    Scenario: Last feed with pending message is on top
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        And Alice has a backend ChatFeed with "Charlie"
        When Alice opens ChatFeed with "Bob" in browser
        And Alice sends a pending message "Pending Bob" via browser
        When Alice navigates back to feed list
        And Alice opens ChatFeed with "Charlie" in browser
        And Alice sends a pending message "Pending Charlie" via browser
        When Alice navigates back to feed list
        Then the personal feed should be at position 0
        And feeds with pending messages should be above feeds without pending messages
