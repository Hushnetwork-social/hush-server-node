@E2E @DocumentVideoAttachmentWalkthrough @FEAT-068
Feature: Document & Video Attachment Walkthrough
    As two users on HushNetwork
    I want to send and receive document and video attachments in a direct chat
    So that file sharing works end-to-end through the blockchain

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    @F4-E2E-001
    Scenario: Alice sends a PDF document to Bob, who sees document card
        # === SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And a browser context for "Bob"
        And "Bob" has created identity via browser

        # === CHAT CREATION ===
        When Alice creates a new chat with "Bob" via browser
        And Bob triggers sync
        Then "Bob" should appear in Alice's feed list
        And "Alice" should appear in Bob's feed list

        # === ATTACH PDF VIA FILE PICKER ===
        When Alice opens the chat with "Bob"
        And Alice attaches a PDF document for "Bob" via file picker
        Then the composer overlay should be visible
        And the composer overlay should show a document preview

        # === SEND PDF WITH TEXT ===
        When Alice types "Meeting notes" in the composer overlay
        And Alice sends from the composer overlay and waits for confirmation
        Then the composer overlay should not be visible

        # === BOB RECEIVES DOCUMENT CARD ===
        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Meeting notes" from Alice
        And Bob should see a document card in the message
        And the document card should show filename "Test-PDF-from-Alice-to-Bob.pdf"

    @F4-E2E-002
    Scenario: Alice sends a video to Bob, who sees video thumbnail with play icon
        # === SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And a browser context for "Bob"
        And "Bob" has created identity via browser

        # === CHAT CREATION ===
        When Alice creates a new chat with "Bob" via browser
        And Bob triggers sync
        Then "Bob" should appear in Alice's feed list
        And "Alice" should appear in Bob's feed list

        # === ATTACH VIDEO VIA FILE PICKER ===
        When Alice opens the chat with "Bob"
        And Alice attaches a video file for "Bob" via file picker
        Then the composer overlay should be visible

        # === SEND VIDEO ===
        When Alice types "Check this video!" in the composer overlay
        And Alice sends from the composer overlay and waits for confirmation
        Then the composer overlay should not be visible

        # === BOB RECEIVES VIDEO THUMBNAIL ===
        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Check this video!" from Alice
        And Bob should see a video element in the message

    @F4-E2E-003
    Scenario: Mixed attachments - image and PDF in carousel
        # === SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And a browser context for "Bob"
        And "Bob" has created identity via browser

        # === CHAT CREATION ===
        When Alice creates a new chat with "Bob" via browser
        And Bob triggers sync
        Then "Bob" should appear in Alice's feed list
        And "Alice" should appear in Bob's feed list

        # === ATTACH IMAGE + PDF ===
        When Alice opens the chat with "Bob"
        And Alice attaches an image and a PDF for "Bob" via file picker
        Then the composer overlay should be visible
        And the composer should show attachment count "2/5"

        # === SEND MIXED ATTACHMENT MESSAGE ===
        When Alice types "Image and document" in the composer overlay
        And Alice sends from the composer overlay and waits for confirmation
        Then the composer overlay should not be visible

        # === BOB RECEIVES MIXED CAROUSEL ===
        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Image and document" from Alice
        And Bob should see thumbnail page indicator "1 / 2"

    @F4-E2E-005
    Scenario: Bob opens video in lightbox, plays, pauses, seeks via progress bar
        # === SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And a browser context for "Bob"
        And "Bob" has created identity via browser

        # === CHAT CREATION ===
        When Alice creates a new chat with "Bob" via browser
        And Bob triggers sync
        Then "Bob" should appear in Alice's feed list
        And "Alice" should appear in Bob's feed list

        # === ATTACH AND SEND VIDEO ===
        When Alice opens the chat with "Bob"
        And Alice attaches a video file for "Bob" via file picker
        Then the composer overlay should be visible

        When Alice types "Watch this!" in the composer overlay
        And Alice sends from the composer overlay and waits for confirmation
        Then the composer overlay should not be visible

        # === BOB RECEIVES VIDEO ===
        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Watch this!" from Alice
        And Bob should see a video element in the message

        # === BOB CLICKS VIDEO → LIGHTBOX OPENS WITH VIDEO PLAYER ===
        When Bob clicks the video element in the message
        Then the lightbox overlay should be visible
        And the lightbox should show a video player
        And the video progress bar should be visible
        And the video time displays should be visible

        # === BOB CLICKS VIDEO TO PLAY → SEES PAUSE ICON ===
        When Bob clicks the video to play
        Then the video should be playing
        And the video pause icon should be visible

        # === BOB CLICKS VIDEO TO PAUSE → SEES PLAY ICON ===
        When Bob clicks the video to pause
        Then the video should be paused
        And the video play icon should be visible in the player

        # === BOB SEEKS TO BEGINNING VIA PROGRESS BAR ===
        When Bob clicks the progress bar at the beginning
        Then the video current time should be near zero

        # === BOB CLOSES LIGHTBOX ===
        When Bob closes the lightbox
        Then the lightbox overlay should not be visible

    @F4-E2E-004
    Scenario: Blocked executable file is rejected
        # === SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And a browser context for "Bob"
        And "Bob" has created identity via browser

        # === CHAT CREATION ===
        When Alice creates a new chat with "Bob" via browser
        And Bob triggers sync

        # === TRY TO ATTACH BLOCKED FILE ===
        When Alice opens the chat with "Bob"
        And Alice tries to attach a blocked executable file
        Then the composer overlay should not be visible
