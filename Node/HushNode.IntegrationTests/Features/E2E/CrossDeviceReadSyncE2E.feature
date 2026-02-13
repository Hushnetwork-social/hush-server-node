@E2E @FEAT-063
Feature: FEAT-063 Cross-Device Read Sync E2E
    As a user with multiple browser sessions
    I want reading messages on one session to update unread badges on another
    So that my unread counts are consistent everywhere

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    # Verify cross-device read sync: Alice reads on Device A, unread clears on Device B
    @F3-E2E-001
    Scenario: Reading on Device A clears unread badge on Device B
        # Setup: Alice creates identity on DeviceA
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        # Bob sends a message to create unread for Alice
        When Bob sends a confirmed backend message "Hello from Bob" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        Then the feed list should show unread badge on ChatFeed with "Bob"
        # Create Device B with same credentials
        Given a second browser context for "Alice" as "DeviceB"
        When DeviceB triggers sync
        Then "DeviceB" feed list should show unread badge on ChatFeed with "Bob"
        # Alice reads on Device A (primary context)
        When Alice opens ChatFeed with "Bob" in browser
        And Alice triggers feed sync
        # Verify Device B clears unread badge after sync
        When DeviceB triggers sync
        Then "DeviceB" feed list should NOT show unread badge on ChatFeed with "Bob"

    # Verify correct unread count when messages arrive after read
    @F3-E2E-002
    Scenario: Correct unread count after post-read messages
        # Setup: Alice creates identity on DeviceA
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        # Bob sends first message
        When Bob sends a confirmed backend message "Message 1" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        # Alice reads on Device A
        When Alice opens ChatFeed with "Bob" in browser
        And Alice triggers feed sync
        Then the feed list should NOT show unread badge on ChatFeed with "Bob"
        # Alice navigates away from Bob's chat (so new messages won't auto-read)
        When Alice opens her personal feed
        # Bob sends another message after Alice read
        When Bob sends a confirmed backend message "Message 2" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        # Device A should show 1 unread (new message after read)
        Then the feed list should show unread badge on ChatFeed with "Bob"

    # CF-002: Reading a feed should NOT change sort order
    @F3-E2E-003
    Scenario: Reading a feed does not change sort order
        # Setup: Alice creates identity on DeviceA
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        And Alice has a backend ChatFeed with "Charlie"
        # Bob's feed gets a message at higher blockIndex -> appears above Charlie
        When Bob sends a confirmed backend message "Hi" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        Then the feed list should show ChatFeed with "Bob" above ChatFeed with "Charlie"
        # Alice reads Bob's feed
        When Alice opens ChatFeed with "Bob" in browser
        And Alice triggers feed sync
        Then the feed list should NOT show unread badge on ChatFeed with "Bob"
        # Sort order unchanged after read
        Then the feed list should show ChatFeed with "Bob" above ChatFeed with "Charlie"

    # EC-003: Messages arrive between read event and next sync on Device B
    @F3-E2E-004
    Scenario: Messages arriving between read and sync are counted as unread
        # Setup: Alice creates identity on DeviceA
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        # Bob sends first message
        When Bob sends a confirmed backend message "Msg 1" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        # Alice reads the feed
        When Alice opens ChatFeed with "Bob" in browser
        And Alice triggers feed sync
        Then the feed list should NOT show unread badge on ChatFeed with "Bob"
        # Alice navigates away, then Bob sends 2 more messages before next sync
        When Alice opens her personal feed
        When Bob sends a confirmed backend message "Msg 2" to ChatFeed(Alice,Bob)
        When Bob sends a confirmed backend message "Msg 3" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        # Should show unread (2 messages after read position)
        Then the feed list should show unread badge on ChatFeed with "Bob"

    # EC-006: Concurrent read position updates converge to max watermark
    @F3-E2E-005
    Scenario: Concurrent read positions converge to highest watermark
        # Setup: Alice creates identity on DeviceA
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has a backend ChatFeed with "Bob"
        # Bob sends 2 messages
        When Bob sends a confirmed backend message "Msg A" to ChatFeed(Alice,Bob)
        When Bob sends a confirmed backend message "Msg B" to ChatFeed(Alice,Bob)
        And Alice triggers feed sync
        Then the feed list should show unread badge on ChatFeed with "Bob"
        # Create DeviceB
        Given a second browser context for "Alice" as "DeviceB"
        When DeviceB triggers sync
        Then "DeviceB" feed list should show unread badge on ChatFeed with "Bob"
        # Alice reads on Device A (reads all messages)
        When Alice opens ChatFeed with "Bob" in browser
        And Alice triggers feed sync
        Then the feed list should NOT show unread badge on ChatFeed with "Bob"
        # Device B syncs and should converge to same read position
        When DeviceB triggers sync
        Then "DeviceB" feed list should NOT show unread badge on ChatFeed with "Bob"
