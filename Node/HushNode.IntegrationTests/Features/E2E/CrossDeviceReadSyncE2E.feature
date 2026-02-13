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
