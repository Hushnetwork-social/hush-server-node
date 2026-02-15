@E2E @SocialWalkthrough
Feature: Social Walkthrough
    As five users on HushNetwork
    I want to create multiple chats, public and private groups, exchange messages, and manage feeds
    So that multi-user social interactions work end-to-end

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    Scenario: Five users interact across chats, groups, and feed ordering
        # =================================================================
        # PHASE 1: Identity Creation (5 users)
        # =================================================================
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        Given a browser context for "Bob"
        And "Bob" has created identity via browser
        Given a browser context for "Charlie"
        And "Charlie" has created identity via browser
        Given a browser context for "Diana"
        And "Diana" has created identity via browser
        Given a browser context for "Eve"
        And "Eve" has created identity via browser

        # =================================================================
        # PHASE 2: Chat Feed Creation via New Chat UI
        # Alice creates chats with Bob, Charlie, Diana, Eve (4 chats)
        # Bob creates chats with Charlie, Diana, Eve (3 chats)
        # Charlie creates chats with Diana, Eve (2 chats)
        # Diana creates chat with Eve (1 chat)
        # Total: 10 chat feeds
        # =================================================================
        When Alice creates a new chat with "Bob" via browser
        And Alice creates a new chat with "Charlie" via browser
        And Alice creates a new chat with "Diana" via browser
        And Alice creates a new chat with "Eve" via browser

        When Bob creates a new chat with "Charlie" via browser
        And Bob creates a new chat with "Diana" via browser
        And Bob creates a new chat with "Eve" via browser

        When Charlie creates a new chat with "Diana" via browser
        And Charlie creates a new chat with "Eve" via browser

        When Diana creates a new chat with "Eve" via browser

        # Sync all users to pick up incoming chat feeds
        When Alice triggers sync
        And Bob triggers sync
        And Charlie triggers sync
        And Diana triggers sync
        And Eve triggers sync

        # Each user should have: 1 personal + 4 chat feeds = 5 feeds
        Then Alice should have 5 feeds in their feed list
        And Bob should have 5 feeds in their feed list
        And Charlie should have 5 feeds in their feed list
        And Diana should have 5 feeds in their feed list
        And Eve should have 5 feeds in their feed list

        # =================================================================
        # PHASE 3: Direct Messaging (selective pairs)
        # =================================================================
        # Alice <-> Bob: 4 messages
        When Alice opens the chat with "Bob"
        And Alice sends message "Hey Bob, how's it going?" and waits for confirmation

        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Hey Bob, how's it going?" from Alice
        When Bob sends message "Great Alice, loving Hush!" and waits for confirmation

        When Alice triggers sync
        Then Alice should see message "Great Alice, loving Hush!" from Bob
        When Alice sends message "The community is growing fast" and waits for confirmation

        When Bob triggers sync
        Then Bob should see message "The community is growing fast" from Alice
        When Bob sends message "Can't wait to see what's next" and waits for confirmation

        When Alice triggers sync
        Then Alice should see message "Can't wait to see what's next" from Bob

        # Charlie <-> Diana: 4 messages
        When Charlie opens the chat with "Diana"
        And Charlie sends message "Hi Diana, nice to meet you!" and waits for confirmation

        When Diana triggers sync
        And Diana opens the chat with "Charlie"
        Then Diana should see message "Hi Diana, nice to meet you!" from Charlie
        When Diana sends message "Hi Charlie, likewise!" and waits for confirmation

        When Charlie triggers sync
        Then Charlie should see message "Hi Charlie, likewise!" from Diana
        When Charlie sends message "Have you joined any groups yet?" and waits for confirmation

        When Diana triggers sync
        Then Diana should see message "Have you joined any groups yet?" from Charlie
        When Diana sends message "Not yet, any recommendations?" and waits for confirmation

        When Charlie triggers sync
        Then Charlie should see message "Not yet, any recommendations?" from Diana

        # Eve <-> Alice: 4 messages
        When Eve opens the chat with "Alice"
        And Eve sends message "Hello Alice, I'm Eve!" and waits for confirmation

        When Alice triggers sync
        And Alice opens the chat with "Eve"
        Then Alice should see message "Hello Alice, I'm Eve!" from Eve
        When Alice sends message "Welcome Eve, glad to have you!" and waits for confirmation

        When Eve triggers sync
        Then Eve should see message "Welcome Eve, glad to have you!" from Alice
        When Eve sends message "This is a great platform!" and waits for confirmation

        When Alice triggers sync
        Then Alice should see message "This is a great platform!" from Eve
        When Alice sends message "It's only getting better!" and waits for confirmation

        When Eve triggers sync
        Then Eve should see message "It's only getting better!" from Alice

        # =================================================================
        # PHASE 4: Unread Badge (Receive While Not Viewing)
        # =================================================================
        # Bob is viewing chat with Charlie (not Alice's chat)
        When Bob opens the chat with "Charlie"
        And Bob sends message "Hey Charlie from Bob!" and waits for confirmation

        # Alice sends message to Bob while Bob is viewing Charlie's chat
        When Alice opens the chat with "Bob"
        And Alice sends message "Bob, check this out!" and waits for confirmation

        When Bob triggers sync
        Then Bob should see unread badge on chat with "Alice"

        # Bob opens Alice's chat — badge should clear
        When Bob opens the chat with "Alice"
        Then Bob should see message "Bob, check this out!" from Alice
        And Bob should NOT see unread badge on chat with "Alice"

        # =================================================================
        # PHASE 5: Group Creation + Pre-Join Messaging
        # =================================================================
        # Alice creates public group "Open Forum" and sends a message BEFORE anyone joins
        Given Alice has created a public group "Open Forum" via browser
        When Alice opens the group "Open Forum"
        And Alice sends message "Early bird message!" and waits for confirmation

        # Bob creates private group "Inner Circle" with Diana+Eve and sends a message
        When Bob creates a private group "Inner Circle" with members "Diana, Eve" via browser
        When Bob opens the group "Inner Circle"
        And Bob sends message "Founders only message!" and waits for confirmation

        # =================================================================
        # PHASE 6: Group Joining (Public Group via invite code)
        # =================================================================
        When Alice opens the group "Open Forum"
        And Alice opens group settings
        Then the invite code should be visible

        # Bob joins Open Forum
        When Bob navigates to the join page with the invite code
        And Bob joins the group and waits for confirmation

        # Charlie joins Open Forum
        When Charlie navigates to the join page with the invite code
        And Charlie joins the group and waits for confirmation

        # Diana joins Open Forum
        When Diana navigates to the join page with the invite code
        And Diana joins the group and waits for confirmation

        # Eve joins Open Forum
        When Eve navigates to the join page with the invite code
        And Eve joins the group and waits for confirmation

        # =================================================================
        # PHASE 7: Key Generation Isolation + Post-Join Messaging
        # =================================================================
        # --- KEY GENERATION ISOLATION: Public Group ---
        # Bob and Charlie joined AFTER "Early bird message!" → should NOT see it
        When Bob triggers sync
        And Bob opens the group "Open Forum"
        Then Bob should NOT see message "Early bird message!" in "Open Forum"

        When Charlie triggers sync
        And Charlie opens the group "Open Forum"
        Then Charlie should NOT see message "Early bird message!" in "Open Forum"

        # Alice (creator, has KeyGen 0) SHOULD still see her message
        When Alice triggers sync
        And Alice opens the group "Open Forum"
        Then Alice should see message "Early bird message!" from Alice

        # --- KEY GENERATION ISOLATION: Private Group ---
        # Diana and Eve were initial members → they share KeyGen 0 → CAN see Bob's message
        When Diana triggers sync
        And Diana opens the group "Inner Circle"
        Then Diana should see message "Founders only message!" from Bob

        When Eve triggers sync
        And Eve opens the group "Inner Circle"
        Then Eve should see message "Founders only message!" from Bob

        # --- POST-JOIN MESSAGING (everyone has current key) ---
        When Alice opens the group "Open Forum"
        And Alice sends message "Welcome to Open Forum everyone!" and waits for confirmation

        When Bob triggers sync
        And Bob opens the group "Open Forum"
        Then Bob should see message "Welcome to Open Forum everyone!" from Alice

        When Charlie triggers sync
        And Charlie opens the group "Open Forum"
        Then Charlie should see message "Welcome to Open Forum everyone!" from Alice

        When Diana triggers sync
        And Diana opens the group "Open Forum"
        Then Diana should see message "Welcome to Open Forum everyone!" from Alice

        When Eve triggers sync
        And Eve opens the group "Open Forum"
        Then Eve should see message "Welcome to Open Forum everyone!" from Alice

        # Inner Circle: Diana sends (post-join, all members have current key)
        When Diana opens the group "Inner Circle"
        And Diana sends message "Hello Inner Circle!" and waits for confirmation

        When Bob triggers sync
        And Bob opens the group "Inner Circle"
        Then Bob should see message "Hello Inner Circle!" from Diana

        When Eve triggers sync
        And Eve opens the group "Inner Circle"
        Then Eve should see message "Hello Inner Circle!" from Diana

        # =================================================================
        # PHASE 8: Feed Ordering + Opening Preserves Order
        # =================================================================
        # Alice sends to Eve → Eve's chat should move to position 1
        When Alice opens the chat with "Eve"
        And Alice sends message "Eve, testing feed ordering!" and waits for confirmation
        And Alice triggers sync

        # The chat with Eve should be at position 1 (after personal feed at 0)
        Then the chat with "Eve" should be at position 1 in Alice's feed list

        # --- SECOND SYNC: Position must persist (regression: stale server blockIndex overwrite) ---
        When Alice triggers sync
        Then the chat with "Eve" should be at position 1 in Alice's feed list

        # --- OPENING FEEDS PRESERVES ORDER ---
        When Alice records the current feed list order
        And Alice opens the chat with "Bob"
        And Alice opens the chat with "Charlie"
        And Alice opens the group "Open Forum"
        And Alice opens the chat with "Eve"
        Then the feed list order should remain unchanged for Alice

        # --- IDLE BLOCKS DON'T CHANGE ORDER ---
        When Alice records the current feed list order
        And 3 blocks are produced without activity
        And Alice triggers sync
        Then the feed list order should remain unchanged for Alice

        # =================================================================
        # PHASE 9: Sequential Reorders + Group Position
        # =================================================================
        # Charlie sends in Open Forum → should move to position 1
        When Charlie opens the group "Open Forum"
        And Charlie sends message "Another message in Open Forum!" and waits for confirmation

        When Alice triggers sync
        And Alice opens the group "Open Forum"
        Then Alice should see message "Another message in Open Forum!" from Charlie

        # Open Forum should now be at position 1 (most recent activity)
        Then the group "Open Forum" should be at position 1 in Alice's feed list

        # --- SEQUENTIAL REORDER ---
        # Bob sends Alice a direct message → his chat should push Open Forum down
        When Bob opens the chat with "Alice"
        And Bob sends message "Hey Alice, quick question!" and waits for confirmation

        When Alice triggers sync
        Then the chat with "Bob" should be at position 1 in Alice's feed list
        And the group "Open Forum" should be at position 2 in Alice's feed list

        # --- SECOND SYNC: Positions must persist (regression: stale server blockIndex overwrite) ---
        When Alice triggers sync
        Then the chat with "Bob" should be at position 1 in Alice's feed list
        And the group "Open Forum" should be at position 2 in Alice's feed list

        # Verify feed count stability (no duplication or loss)
        # Alice: 1 personal + 4 chats + 1 Open Forum = 6 feeds
        Then Alice should have 6 feeds in their feed list

        # =================================================================
        # PHASE 10: Replies in Group Feed
        # =================================================================
        # Alice replies to her own welcome message in Open Forum
        When Alice opens the group "Open Forum"
        And Alice replies to message "Welcome to Open Forum everyone!" with "This is a reply in a group!"

        # Bob sees the reply with reply preview
        When Bob triggers sync
        And Bob opens the group "Open Forum"
        Then Bob should see message "This is a reply in a group!" from Alice
        And the reply to "Welcome to Open Forum everyone!" should be visible with text "This is a reply in a group!"

        # Charlie (non-participant in the reply) also sees it
        When Charlie triggers sync
        And Charlie opens the group "Open Forum"
        Then Charlie should see message "This is a reply in a group!" from Alice
        And the reply to "Welcome to Open Forum everyone!" should be visible with text "This is a reply in a group!"

        # =================================================================
        # PHASE 11: Mentions in Group Feed
        # =================================================================
        # Bob navigates away from Open Forum so the mention badge is visible
        When Bob opens the chat with "Alice"

        # Alice mentions Bob in Open Forum
        When Alice opens the group "Open Forum"
        And Alice sends message mentioning "Bob" with "what do you think?" and waits for confirmation

        # Bob syncs while viewing Alice's chat — notification toast + mention badge should appear
        When Bob triggers sync
        Then Bob should see a notification toast from "Alice" in group "Open Forum" with "@Bob"
        And Bob should see mention badge on group "Open Forum"

        # Bob opens the group and sees the mention rendered
        When Bob opens the group "Open Forum"
        Then Bob should see mention "@Bob" in message containing "what do you think?"

        # After reading, mention badge should clear
        Then Bob should NOT see mention badge on group "Open Forum"

        # Charlie (NOT mentioned) also sees the mention rendered
        When Charlie triggers sync
        And Charlie opens the group "Open Forum"
        Then Charlie should see mention "@Bob" in message containing "what do you think?"

        # =================================================================
        # PHASE 12: Link Previews in Group Feed + Notification Toast
        # =================================================================
        # Bob navigates away from Open Forum so notification toast is visible
        When Bob opens the chat with "Alice"

        # Alice sends a URL message in Open Forum
        When Alice opens the group "Open Forum"
        And Alice sends message "Everyone check http://www.hushnetwork.social awesome project!" and waits for confirmation
        Then Alice should see a link preview in message containing "Everyone check"

        # Bob syncs — notification toast shows URL text, NOT link metadata
        When Bob triggers sync
        Then Bob should see a notification toast from "Alice" in group "Open Forum" with "hushnetwork.social"
        And the notification toast should not contain link preview metadata

        # Bob opens group and sees the link preview card
        When Bob opens the group "Open Forum"
        Then Bob should see a link preview in message containing "Everyone check"

        # Two links in group - verify carousel with navigation
        When Alice sends message "Compare http://www.hushnetwork.social and https://www.google.com" and waits for confirmation
        Then Alice should see 2 link previews in message containing "Compare"

        # At position 1/2: Previous disabled, Next enabled
        Then Alice should see previous link preview button disabled in message containing "Compare"
        And Alice should see next link preview button enabled in message containing "Compare"

        # Navigate to second link preview
        When Alice clicks next link preview in message containing "Compare"
        Then Alice should see link preview 2 of 2 in message containing "Compare"

        # At position 2/2: Previous enabled, Next disabled
        Then Alice should see previous link preview button enabled in message containing "Compare"
        And Alice should see next link preview button disabled in message containing "Compare"

        # Charlie also sees the link previews
        When Charlie triggers sync
        And Charlie opens the group "Open Forum"
        Then Charlie should see a link preview in message containing "Everyone check"
        And Charlie should see 2 link previews in message containing "Compare"
