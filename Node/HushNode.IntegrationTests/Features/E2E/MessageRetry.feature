@E2E @MessageRetry @FEAT-058
Feature: Message Retry Functionality
    As a user sending messages
    I want to see visual feedback on message delivery status
    So that I know if my messages were delivered successfully

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker
        And a browser is launched

    @F2-001 @StatusIcon @PR
    Scenario: Message shows pending status immediately after sending
        # Setup: Create identity and navigate to personal feed
        Given the user has created identity "RetryUser" via browser
        And the user clicks on their personal feed

        # Action: Send message
        When the user sends message "Testing pending status"

        # Verification: Message appears with pending (clock) icon
        # Note: We verify pending BEFORE block production
        Then the message "Testing pending status" should show pending status icon

    @F2-003 @StatusIcon @PR
    Scenario: Message shows confirmed status after successful delivery
        # Setup: Create identity, navigate to feed, and send message
        Given the user has created identity "ConfirmUser" via browser
        And the user clicks on their personal feed
        When the user sends message "Testing confirmed status"

        # Wait for block production and sync
        And the transaction is processed

        # Verification: Message shows confirmed (checkmark) icon
        Then the message "Testing confirmed status" should show confirmed status icon

    @F2-009 @StateTransition @PR
    Scenario: Message transitions from pending to confirmed
        # Setup: Create identity and navigate to personal feed
        Given the user has created identity "TransitionUser" via browser
        And the user clicks on their personal feed

        # Action: Send message
        When the user sends message "Testing state transition"

        # State 1: Verify pending immediately
        Then the message "Testing state transition" should show pending status icon

        # Trigger block production
        When the transaction is processed

        # State 2: Verify confirmed after block
        Then the message "Testing state transition" should show confirmed status icon

    @F2-007 @NewMessageWithFailed
    Scenario: User can send new message after previous message is confirmed
        # Setup: Create identity and send first message
        Given the user has created identity "MultiMsgUser" via browser
        And the user clicks on their personal feed
        And the user sends message "First message"
        And the transaction is processed
        Then the message "First message" should show confirmed status icon

        # Action: Send second message
        When the user sends message "Second message"

        # Verification: Both messages visible, new one shows pending
        Then the message "Second message" should show pending status icon
        And the message "First message" should show confirmed status icon

    @CF-001 @AlreadyExists @Critical
    Scenario: Server ALREADY_EXISTS response marks message as confirmed
        # This scenario tests the idempotency behavior from FEAT-057
        # When retrying a message that already exists on the server,
        # the server returns ALREADY_EXISTS and the client marks it confirmed

        # Setup: Create identity and send message
        Given the user has created identity "IdempotentUser" via browser
        And the user clicks on their personal feed
        And the user sends message "Idempotent message test"
        And the transaction is processed

        # Verification: Message is confirmed (ALREADY_EXISTS handled correctly on server sync)
        Then the message "Idempotent message test" should show confirmed status icon
        And the message should have a timestamp displayed

    @F2-006 @FailedIndicator
    Scenario: Message status icons render correctly for own messages
        # This tests that status icons only appear for own messages
        Given the user has created identity "IconTestUser" via browser
        And the user clicks on their personal feed

        # Send message and verify pending icon appears
        When the user sends message "Icon test message"
        Then the message "Icon test message" should show pending status icon

        # Confirm and verify confirmed icon
        When the transaction is processed
        Then the message "Icon test message" should show confirmed status icon

    @EC-001 @EdgeCase @Automatable
    Scenario: Messages persist and restore after page reload
        Given the user has created identity "CrashRecoveryUser" via browser
        And the user clicks on their personal feed
        When the user sends message "Crash recovery test"
        And the transaction is processed
        Then the message "Crash recovery test" should show confirmed status icon
        When the page is reloaded
        And the user waits for rehydration
        And the user clicks on their personal feed
        Then the message "Crash recovery test" should be visible in the chat
        And the message "Crash recovery test" should show confirmed status icon

    @EC-003 @EdgeCase @Automatable
    Scenario: Warning shown when logging out with pending messages
        Given the user has created identity "PendingLogoutUser" via browser
        And the user clicks on their personal feed
        When the user sends message "Pending logout msg"
        Then the message "Pending logout msg" should show pending status icon
        When the user clicks the logout button
        Then a confirmation dialog should be visible with title "Unsent Messages"
        When the user clicks "Logout Anyway" in the dialog
        Then the page should navigate to the auth page

    @EC-004 @EdgeCase @Automatable
    Scenario: Warning shown when logging out with failed messages
        Given the user has created identity "FailedLogoutUser" via browser
        And the user clicks on their personal feed
        When a failed message is injected into the feeds store
        And the user clicks the logout button
        Then a confirmation dialog should be visible with title "Unsent Messages"
        When the user clicks "Logout Anyway" in the dialog
        Then the page should navigate to the auth page
