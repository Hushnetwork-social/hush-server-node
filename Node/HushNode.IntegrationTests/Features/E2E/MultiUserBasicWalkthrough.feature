@E2E @MultiUser @PR
Feature: Multi-User Basic Walkthrough
    As a group member
    I want to see messages from other members
    So that we can communicate in the group

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    @TwoUsers
    Scenario: Multi-user group messaging with explicit synchronization
        # === ALICE SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        # Screenshot: Alice identity and personal feed created

        And Alice has created a public group "Team Chat" via browser
        # Screenshot: Group shows in FeedList

        # === ALICE SENDS FIRST MESSAGE (before getting invite code) ===
        When Alice opens the group "Team Chat"
        # Screenshot: Alice selected the FeedGroup

        And Alice sends message "Setting up the group!" and waits for confirmation
        # Screenshot: Message confirmed (checkmark visible)

        # === GET INVITE CODE ===
        When Alice opens group settings
        Then the invite code should be visible
        # Screenshot: Invite code visible

        # === BOB SETUP ===
        Given a browser context for "Bob"
        And "Bob" has created identity via browser
        # Screenshot: Bob identity created

        # === BOB JOINS GROUP ===
        When Bob navigates to the join page with the invite code
        # Screenshot: Bob found the group to join

        And Bob joins the group and waits for confirmation
        Then "Team Chat" should appear in Bob's feed list
        # Screenshot: Bob joined, group in FeedList

        # Bob should NOT see Alice's first message (sent before he joined)
        And Bob should NOT see message "Setting up the group!" in "Team Chat"

        # === BOB SENDS MESSAGE ===
        When Bob sends message "Hi Alice, I joined!" and waits for confirmation
        # Screenshot: Bob's message confirmed

        # === ALICE SYNCS VIA PERSONAL FEED (workaround for KeyGen sync order) ===
        # The KeyGenerations sync happens globally, but decryption depends on having
        # the keys BEFORE messages are fetched. By going to Personal Feed first,
        # we trigger a full sync that fetches new KeyGenerations, then when we
        # open the Group Feed, the keys are already available for decryption.
        When Alice opens her personal feed
        # Screenshot: Alice on personal feed

        And Alice sends message "Just a sync trigger..." and waits for confirmation
        # Screenshot: Alice's personal message confirmed (full sync completed)

        # === VERIFY ALICE HAS 2 KEY GENERATIONS (CRITICAL ASSERTION) ===
        # This is the same assertion as the Twin Test - Alice MUST have KeyGen 0 AND KeyGen 1
        Then Alice should have exactly 2 KeyGenerations for "Team Chat" via gRPC
        # If this fails, the server is not returning KeyGen 1 to Alice

        # === VERIFY ALICE RECEIVES BOB'S MESSAGE WITH KEYGENERATION=1 ===
        Then Alice should receive Bob's message "Hi Alice, I joined!" with KeyGeneration 1 via gRPC
        # If this fails, the server is not returning the message or KeyGeneration is wrong

        # === VERIFY ALICE CAN DECRYPT THE MESSAGE ===
        Then Alice should be able to decrypt the message using KeyGeneration 1
        # If this fails, the AES key decryption or message decryption is broken

        # === NOW ALICE CAN SEE BOB'S MESSAGE ===
        When Alice opens the group "Team Chat"
        # Screenshot: Alice opened group after KeyGen sync

        And Alice triggers sync
        Then Alice should see that Bob joined the group

        # === DEBUG: Dump client-side message state ===
        Then dump Alice's message state for "Team Chat"

        And Alice should see message "Hi Alice, I joined!" from Bob
        # Screenshot: Alice sees Bob and his message

        # === ALICE REACTS TO BOB'S MESSAGE AND REPLIES ===
        When Alice adds reaction to Bob's message
        And Alice sends message "Welcome to the team!" and waits for confirmation
        # Screenshot: Alice's reaction and reply confirmed

        # === BOB SEES ALICE'S REACTION AND REPLY, THEN REACTS BACK ===
        # === VERIFY BOB HAS KEYGENERATION 1 ===
        Then Bob should have exactly 1 KeyGeneration for "Team Chat" via gRPC
        # Bob only joined at KeyGen 1, so he should only have KeyGen 1

        # === VERIFY ALICE'S MESSAGE WAS SENT WITH KEYGENERATION 1 ===
        Then Alice's message "Welcome to the team!" should be on server with KeyGeneration 1

        # === VERIFY BOB RECEIVES ALICE'S MESSAGE ===
        Then Bob should receive Alice's message "Welcome to the team!" with KeyGeneration 1 via gRPC

        # === VERIFY BOB CAN DECRYPT THE MESSAGE ===
        Then Bob should be able to decrypt the message using KeyGeneration 1

        When Bob triggers sync
        Then Bob should see message "Welcome to the team!" from Alice
        When Bob adds reaction to Alice's message
        # Screenshot: Bob sees Alice's reply and reacted to it

        # === ALICE SEES BOB'S REACTION ===
        When Alice triggers sync
        # Screenshot: Alice sees Bob's reaction (test complete)
