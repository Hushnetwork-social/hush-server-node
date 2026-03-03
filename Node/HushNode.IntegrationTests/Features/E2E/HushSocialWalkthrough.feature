@E2E @HushSocial @EPIC008
Feature: HushSocial end-to-end walkthrough
  As a Hush user
  I want to use HushSocial from navigation through posting and interactions
  So that EPIC-008 behavior works in real user flows

  Background:
    Given a HushServerNode at block 1
    And HushWebClient is running in Docker
    And a browser context for "Owner"
    And "Owner" has created identity via browser
    And a browser context for "FollowerA"
    And "FollowerA" has created identity via browser
    And a browser context for "FollowerB"
    And "FollowerB" has created identity via browser

  @FEAT-084
  Scenario: Community navigation is replaced by HushSocial shell
    When Owner opens the application
    Then Owner should see HushSocial in main navigation
    And Owner should not see Community in main navigation
    And Owner should see the HushSocial feed shell layout

  @FEAT-085 @FEAT-090
  Scenario: Close profile onboarding and follower approval flow
    Given Owner opens HushSocial privacy settings
    When Owner sets profile mode to Close
    Then Owner should see Inner Circle created automatically
    When FollowerA requests to follow Owner via browser
    Then Owner should see a pending follow request from FollowerA
    When Owner accepts follow request from FollowerA via browser
    Then Owner should see FollowerA in Inner Circle members

  @FEAT-086
  Scenario: Open and close post creation with permalink behavior
    Given Owner opens HushSocial composer
    When Owner creates Open post "Hello public world" via browser
    Then Owner should be able to copy permalink for post "Hello public world"
    When Owner creates Close post "Hello inner circle" for Inner Circle via browser
    Then Owner should be able to copy permalink for post "Hello inner circle"
    When FollowerB opens permalink for post "Hello public world"
    Then FollowerB should see post "Hello public world"
    When FollowerB opens permalink for post "Hello inner circle"
    Then FollowerB should see a generic access denied message
    When FollowerA opens permalink for post "Hello inner circle"
    Then FollowerA should see post "Hello inner circle"

  @FEAT-086
  Scenario: Media post upload constraints and success path
    Given Owner opens HushSocial composer
    When Owner attaches valid media files and posts "Media update"
    Then Owner should see post "Media update" in timeline
    When Owner attempts to attach too many media files in one post
    Then Owner should see a media count limit validation message
    When Owner attempts to attach an oversized media file
    Then Owner should see a media size limit validation message

  @FEAT-087 @FEAT-088
  Scenario: Reactions comments and single-level replies on authorized post
    Given Owner has created Open post "Discuss architecture" via browser
    When FollowerA reacts to post "Discuss architecture" with emoji "thumbs_up" via browser
    And FollowerA comments "Looks good" on post "Discuss architecture" via browser
    And Owner replies "Thanks for feedback" to comment "Looks good" via browser
    Then both users should see comment "Looks good"
    And both users should see reply "Thanks for feedback"
    And the reply action should be limited to single-level depth

  @FEAT-089
  Scenario: Guest interaction opens account creation overlay on public post
    Given an unauthenticated browser session
    And Owner has created Open post "Guest visible content" via browser
    When guest attempts to react to post "Guest visible content" via browser
    Then guest should see account creation overlay
    When guest attempts to comment on post "Guest visible content" via browser
    Then guest should see account creation overlay

  @FEAT-090
  Scenario: Follow graph drives following-first timeline
    Given FollowerA follows Owner via browser
    And Owner creates Open post "Update from owner" via browser
    And FollowerB creates Open post "Update from other user" via browser
    When FollowerA opens home timeline
    Then post "Update from owner" should appear before post "Update from other user"

  @FEAT-091
  Scenario: Notification preferences and per-circle mute in real flow
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA via browser
    And FollowerA enables Close post notifications via browser
    And FollowerA mutes circle "Inner Circle" via browser
    When Owner publishes Close post "Muted circle notice" to Inner Circle via browser
    Then FollowerA should not see notification for post "Muted circle notice"
    When FollowerA unmutes circle "Inner Circle" via browser
    And Owner publishes Close post "Active circle notice" to Inner Circle via browser
    Then FollowerA should see an in-app notification for post "Active circle notice"
