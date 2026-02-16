@E2E @ImageAttachmentWalkthrough @FEAT-067
Feature: Image Attachment Walkthrough
    As two users on HushNetwork
    I want to send and receive image attachments in a direct chat
    So that image sharing works end-to-end through the blockchain

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    @F3-E2E-001
    Scenario: Alice sends image attachments to Bob, who views them in lightbox
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

        # === F3-003/F3-004: FILE PICKER OPENS COMPOSER OVERLAY ===
        When Alice opens the chat with "Bob"
        And Alice attaches image 1 for "Bob" via file picker
        Then the composer overlay should be visible
        And the composer overlay should show an image preview
        And the composer send button should be visible
        And the composer close button should be visible

        # === F3-004: CLOSE OVERLAY CANCELS ATTACHMENT ===
        When Alice closes the composer overlay
        Then the composer overlay should not be visible

        # === F3-007: SEND IMAGE WITH TEXT ===
        When Alice attaches image 1 for "Bob" via file picker
        And Alice types "Check this out!" in the composer overlay
        And Alice sends from the composer overlay and waits for confirmation
        Then the composer overlay should not be visible

        # === F3-007/F3-009: BOB RECEIVES MESSAGE WITH THUMBNAIL ===
        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Check this out!" from Alice
        And Bob should see attachment "Image-1-from-Alice-to-Bob.png" in the thumbnail

        # === F3-010: LIGHTBOX OPEN/CLOSE ===
        When Bob clicks the thumbnail for "Image-1-from-Alice-to-Bob.png"
        Then the lightbox overlay should be visible
        And the lightbox should show attachment "Image-1-from-Alice-to-Bob.png"
        And the lightbox close button should be visible

        When Bob closes the lightbox
        Then the lightbox overlay should not be visible

        # === F3-008: ATTACHMENT-ONLY MESSAGE (NO TEXT) ===
        When Alice attaches image 2 for "Bob" via file picker
        And Alice sends from the composer overlay and waits for confirmation

        When Bob triggers sync
        Then Bob should see attachment "Image-2-from-Alice-to-Bob.png" in the thumbnail
        And Bob should see 2 image thumbnails in the chat

    @F3-E2E-002
    Scenario: Alice sends 5 images in one message, both users navigate carousel and lightbox
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

        # === ATTACH 5 IMAGES AT ONCE ===
        When Alice opens the chat with "Bob"
        And Alice attaches images 1 through 5 for "Bob" via file picker
        Then the composer overlay should be visible
        And the composer should show attachment count "5/5"
        And the composer should show page indicator "1 / 5"

        # === NAVIGATE COMPOSER CAROUSEL ===
        When Alice navigates to the next composer preview
        Then the composer should show page indicator "2 / 5"
        When Alice navigates to the next composer preview
        Then the composer should show page indicator "3 / 5"
        When Alice navigates to the previous composer preview
        Then the composer should show page indicator "2 / 5"

        # === SEND 5-IMAGE MESSAGE ===
        When Alice types "Five images!" in the composer overlay
        And Alice sends from the composer overlay and waits for confirmation
        Then the composer overlay should not be visible

        # === BOB RECEIVES 5 IMAGES ===
        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Five images!" from Alice
        And Bob should see attachment "Image-1-from-Alice-to-Bob.png" in the thumbnail

        # === BOB NAVIGATES THUMBNAIL CAROUSEL ===
        Then Bob should see thumbnail page indicator "1 / 5"
        And the current thumbnail should show "Image-1-from-Alice-to-Bob.png"
        When Bob clicks the next thumbnail arrow
        Then Bob should see thumbnail page indicator "2 / 5"
        And the current thumbnail should show "Image-2-from-Alice-to-Bob.png"
        When Bob clicks the next thumbnail arrow
        Then Bob should see thumbnail page indicator "3 / 5"
        And the current thumbnail should show "Image-3-from-Alice-to-Bob.png"
        When Bob clicks the previous thumbnail arrow
        Then Bob should see thumbnail page indicator "2 / 5"
        And the current thumbnail should show "Image-2-from-Alice-to-Bob.png"

        # === BOB OPENS LIGHTBOX FROM CAROUSEL POSITION (was at 2/5) ===
        When Bob clicks the current thumbnail image
        Then the lightbox overlay should be visible
        And the lightbox should show page indicator "2 / 5"
        And the lightbox should show attachment "Image-2-from-Alice-to-Bob.png"

        # === BOB NAVIGATES LIGHTBOX WITH ARROW KEYS ===
        When Bob presses the right arrow key
        Then the lightbox should show page indicator "3 / 5"
        And the lightbox should show attachment "Image-3-from-Alice-to-Bob.png"
        When Bob presses the right arrow key
        Then the lightbox should show page indicator "4 / 5"
        And the lightbox should show attachment "Image-4-from-Alice-to-Bob.png"

        # === NAVIGATE BACK WITH LEFT ARROW KEY ===
        When Bob presses the left arrow key
        Then the lightbox should show page indicator "3 / 5"
        And the lightbox should show attachment "Image-3-from-Alice-to-Bob.png"

        # === NAVIGATE TO FIRST IMAGE (mix arrows and buttons) ===
        When Bob presses the left arrow key
        And Bob clicks the previous lightbox arrow
        Then the lightbox should show page indicator "1 / 5"
        And the lightbox should show attachment "Image-1-from-Alice-to-Bob.png"

        # === NAVIGATE TO LAST IMAGE ===
        When Bob clicks the next lightbox arrow
        And Bob presses the right arrow key
        And Bob clicks the next lightbox arrow
        And Bob presses the right arrow key
        Then the lightbox should show page indicator "5 / 5"
        And the lightbox should show attachment "Image-5-from-Alice-to-Bob.png"

        # === CLOSE LIGHTBOX ===
        When Bob closes the lightbox
        Then the lightbox overlay should not be visible

    @F3-E2E-003
    Scenario: Adding images incrementally via file picker and paste auto-navigates composer carousel
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

        # === ATTACH FIRST IMAGE VIA FILE PICKER ===
        When Alice opens the chat with "Bob"
        And Alice attaches image 1 for "Bob" via file picker
        Then the composer overlay should be visible
        And the composer should show attachment count "1/5"
        # Single image - no carousel, verify image directly
        And the current composer preview should show "Image-1-from-Alice-to-Bob.png"

        # === ADD SECOND IMAGE VIA FILE PICKER (composer already open) ===
        # Carousel should auto-navigate to the newly added image
        When Alice attaches image 2 for "Bob" via file picker
        Then the composer should show attachment count "2/5"
        And the composer should show page indicator "2 / 2"
        And the current composer preview should show "Image-2-from-Alice-to-Bob.png"

        # === PASTE THIRD IMAGE VIA CTRL+V (composer already open) ===
        # Carousel should auto-navigate to the pasted image
        When Alice pastes image 3 for "Bob" into the composer
        Then the composer should show attachment count "3/5"
        And the composer should show page indicator "3 / 3"
        And the current composer preview should show "Image-3-from-Alice-to-Bob.png"

        # === NAVIGATE BACK TO VERIFY ALL IMAGES ARE PRESENT ===
        When Alice navigates to the previous composer preview
        Then the composer should show page indicator "2 / 3"
        And the current composer preview should show "Image-2-from-Alice-to-Bob.png"
        When Alice navigates to the previous composer preview
        Then the composer should show page indicator "1 / 3"
        And the current composer preview should show "Image-1-from-Alice-to-Bob.png"

        # === SEND AND VERIFY BOB RECEIVES 3 IMAGES ===
        When Alice types "Incremental add!" in the composer overlay
        And Alice sends from the composer overlay and waits for confirmation
        Then the composer overlay should not be visible

        When Bob triggers sync
        And Bob opens the chat with "Alice"
        Then Bob should see message "Incremental add!" from Alice
        And Bob should see thumbnail page indicator "1 / 3"
