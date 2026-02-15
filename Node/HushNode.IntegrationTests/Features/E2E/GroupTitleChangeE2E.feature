@E2E @GroupTitleChange
Feature: Group Title Change via Group Settings
    As a group admin
    I want to change the group title through the Group Settings panel
    So that all participants see the updated group name

    Background:
        Given a HushServerNode at block 1
        And HushWebClient is running in Docker

    @AdminChangesTitle
    Scenario: Admin changes group title and sees the update
        Given a browser is launched
        Given the user has created identity "Alice" via browser
        And the user has created a public group "Original Title" via browser

        # Action: Open the group, change its title via Settings panel
        When the user clicks on the group feed "Original Title"
        And the user opens group settings via settings button
        And the user changes the group title to "Renamed Group"
        And the user saves the group settings

        # Verification: Feed list shows the updated name
        Then the feed list should contain a group feed "Renamed Group"

    @ParticipantSeesRename
    Scenario: Participant sees updated group title after admin renames
        # === ALICE SETUP ===
        Given a browser context for "Alice"
        And "Alice" has created identity via browser
        And Alice has created a public group "Original Title" via browser

        # === GET INVITE CODE ===
        When Alice opens the group "Original Title"
        And Alice opens group settings
        Then the invite code should be visible

        # === BOB SETUP ===
        Given a browser context for "Bob"
        And "Bob" has created identity via browser

        # === BOB JOINS GROUP ===
        When Bob navigates to the join page with the invite code
        And Bob joins the group and waits for confirmation
        Then "Original Title" should appear in Bob's feed list

        # === ALICE CHANGES GROUP TITLE ===
        When Alice opens the group "Original Title"
        And Alice opens group settings via settings button
        And Alice changes the group title to "Renamed Group"
        And Alice saves the group settings
        Then "Renamed Group" should appear in Alice's feed list

        # === BOB SEES UPDATED TITLE ===
        When Bob triggers sync
        Then "Renamed Group" should appear in Bob's feed list
