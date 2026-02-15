@E2E @DirectChatWalkthrough
Feature: Direct Chat Walkthrough
    As two users on HushNetwork
    I want to create a direct chat, exchange messages, reply to specific messages, and react
    So that peer-to-peer communication works end-to-end

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    Scenario: Two users create a direct chat, exchange messages, react, and reply
        # === SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And a browser context for "Bob"
        And "Bob" has created identity via browser

        # === CHAT CREATION (via New Chat UI) ===
        When Alice creates a new chat with "Bob" via browser
        And Bob triggers sync
        Then "Bob" should appear in Alice's feed list
        And "Alice" should appear in Bob's feed list

        # === CONVERSATION (4 messages, alternating) ===
        When Alice opens the chat with "Bob"
        And Alice sends message "Hey Bob, welcome to Hush!" and waits for confirmation

        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Hey Bob, welcome to Hush!" from Alice

        When Bob sends message "Thanks Alice! Excited to be here." and waits for confirmation

        When Alice triggers sync
        Then Alice should see message "Thanks Alice! Excited to be here." from Bob

        When Alice sends message "Let me show you something cool" and waits for confirmation

        When Bob triggers sync
        Then Bob should see message "Let me show you something cool" from Alice

        When Bob sends message "What is it?" and waits for confirmation

        When Alice triggers sync
        Then Alice should see message "What is it?" from Bob

        # === REPLY TO SPECIFIC MESSAGE ===
        When Alice replies to message "What is it?" with "Watch this reaction!"

        When Bob triggers sync
        Then Bob should see message "Watch this reaction!" from Alice
        And the reply to "What is it?" should be visible with text "Watch this reaction!"

        # === REACTIONS ===
        When Alice adds reaction to Bob's message "Thanks Alice! Excited to be here."
        And the transaction is processed

        When Bob triggers sync
        Then Bob should see a reaction on message "Thanks Alice! Excited to be here."

        When Bob adds reaction to Alice's message "Watch this reaction!"
        And the transaction is processed

        When Alice triggers sync
        Then Alice should see a reaction on message "Watch this reaction!"

        # === LINK PREVIEWS ===
        # Single link - verify preview card appears with metadata
        When Alice sends message "Check this out http://www.hushnetwork.social" and waits for confirmation
        Then Alice should see a link preview in message containing "Check this out"

        When Bob triggers sync
        Then Bob should see a link preview in message containing "Check this out"

        # Two links - verify carousel navigation between previews
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

        When Bob triggers sync
        Then Bob should see 2 link previews in message containing "Compare"
