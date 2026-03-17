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

  @FEAT-084 @HS-E2E-084-NAV
  Scenario: Community navigation is replaced by HushSocial shell
    When Owner opens the application
    Then Owner should see HushSocial in main navigation
    And Owner should not see Community in main navigation
    And Owner should see the HushSocial feed shell layout

  @FEAT-085 @FEAT-090 @ignore @HS-E2E-085-ONBOARDING
  Scenario: Close profile onboarding and follower approval flow
    Given Owner opens HushSocial privacy settings
    When Owner sets profile mode to Close
    Then Owner should see Inner Circle created automatically
    When FollowerA requests to follow Owner via browser
    Then Owner should see a pending follow request from FollowerA
    When Owner accepts follow request from FollowerA via browser
    Then Owner should see FollowerA in Inner Circle members

  @FEAT-085 @HS-E2E-085-CIRCLE-REMOVAL
  Scenario: Circle removal rotates keys and revoked follower loses latest key access
    Given Owner opens HushSocial privacy settings
    And Owner sets profile mode to Close
    And Owner has approved followers "FollowerA, FollowerB" via browser
    And Owner has created FEAT-085 circle "Dev Circle" via backend
    And Owner has added "FollowerA, FollowerB" to FEAT-085 circle "Dev Circle" via backend
    And FollowerB is actively viewing FEAT-085 circle "Dev Circle"
    When Owner removes FollowerB from FEAT-085 circle "Dev Circle" via backend
    Then FEAT-085 key generation for circle "Dev Circle" should be incremented
    And FollowerB should not have FEAT-085 latest key access to circle "Dev Circle"
    And FollowerA should have FEAT-085 latest key access to circle "Dev Circle"

  @FEAT-085 @HS-E2E-085-BOOTSTRAP-STABLE
  Scenario: Repeated bootstrap sync with unchanged followers does not rotate keys
    Given Owner opens HushSocial privacy settings
    And Owner sets profile mode to Close
    And Owner has approved followers "FollowerA" via browser
    When Owner triggers FEAT-085 bootstrap sync twice with unchanged followers
    Then Owner Inner Circle key generation should remain stable after repeated bootstrap

  @FEAT-086 @HS-E2E-086-PERMALINK
  @LONG_RUNNING
  Scenario: Open post creation with permalink behavior
    Given Owner opens HushSocial composer
    When Owner creates Open post "Hello public world" via browser
    Then Owner should be able to copy permalink for post "Hello public world"
    When FollowerA opens HushSocial FeedWall
    Then FollowerA should see Open post "Hello public world" authored by "Owner"
    When FollowerA opens post detail for "Hello public world" from FeedWall
    Then FollowerA should see post detail overlay for "Hello public world" authored by "Owner"
    When FollowerA opens permalink for post "Hello public world"
    Then FollowerA should see permalink post "Hello public world"
    When FollowerB opens permalink for post "Hello public world"
    Then FollowerB should see permalink post "Hello public world"
    And FollowerB should see full-page permalink layout
    When FollowerB navigates from permalink to HushSocial FeedWall
    Then FollowerB should see Open post "Hello public world" authored by "Owner"
    When FollowerB opens post detail for "Hello public world" from FeedWall
    Then FollowerB should see post detail overlay for "Hello public world" authored by "Owner"

  @FEAT-086 @HS-E2E-086-AUTHOR
  Scenario: Public post shows author identity in FeedWall for another user
    Given Owner opens HushSocial composer
    When Owner creates Open post "Public identity author check" via browser
    And FollowerB opens HushSocial FeedWall
    Then FollowerB should see Open post "Public identity author check" authored by "Owner"

  @FEAT-086 @HS-E2E-086-AUDIENCE
  @LONG_RUNNING
  Scenario: Circle audience visibility across followers
    When Owner creates a new chat with "FollowerA" via browser
    And Owner creates a new chat with "FollowerB" via browser
    And Owner switches from HushSocial to HushFeeds
    Then Owner should not see Inner Circle in HushFeeds feed list
    When Owner has approved followers "FollowerA, FollowerB" via browser
    Then Owner should not see Inner Circle in HushFeeds feed list
    When Owner opens HushSocial Following page
    Then Owner should see following members "FollowerA, FollowerB" tagged with "Inner Circle"
    When Owner creates circle "SpecialCircle" via browser
    And Owner adds "FollowerA" to circle "SpecialCircle" via browser
    And Owner opens HushSocial composer
    And Owner creates Open post "Public post" via backend
    And Owner creates Close post "Special circle post" for circle "SpecialCircle" via backend
    When Owner opens HushSocial FeedWall
    Then Owner should see FeedWall post "Public post"
    And Owner should see FeedWall post "Special circle post"
    When FollowerA opens HushSocial FeedWall
    Then FollowerA should see FeedWall post "Public post"
    And FollowerA should see FeedWall post "Special circle post"
    And FollowerA should see audience badge "Public" for FeedWall post "Public post"
    And FollowerA should see audience badge "Private" for FeedWall post "Special circle post"
    And FollowerA should not see audience badge "SpecialCircle" for FeedWall post "Special circle post"
    And FollowerA should not see truncated audience badge for FeedWall post "Special circle post"
    When FollowerB opens HushSocial FeedWall
    Then FollowerB should see FeedWall post "Public post"
    And FollowerB should see audience badge "Public" for FeedWall post "Public post"
    And FollowerB should not see FeedWall post "Special circle post"

  @FEAT-086 @HS-E2E-086-MEDIA
  @LONG_RUNNING
  Scenario: Media post upload constraints and success path
    Given Owner opens HushSocial composer
    When Owner attaches image 1 and animated GIF 1 via file picker
    Then Owner should see 2 media items in composer
    When Owner drags and drops video into HushSocial composer
    Then Owner should see 3 media items in composer
    When Owner pastes image 2 into HushSocial composer
    Then Owner should see 4 media items in composer
    When Owner attempts to attach too many media files in one post
    Then Owner should see a media count limit validation message
    When Owner removes one media item from composer
    And Owner attempts to attach an oversized media file
    Then Owner should see a media size limit validation message
    When Owner creates Open post "Media update" via browser
    Then Owner should see FeedWall post "Media update"

  @FEAT-087 @HS-E2E-087-REACTION-FLOW
  @LONG_RUNNING
  Scenario: Authenticated user reacts to authorized public post
    Given Owner has created Open post "Discuss architecture" via browser
    And FollowerA browser has approved FEAT-087 reaction circuit artifacts available
    When FollowerA reacts to post "Discuss architecture" with emoji "thumbs_up" via browser
    Then FollowerA should see reaction count 1 on post "Discuss architecture"
    And Owner should see reaction count 1 on post "Discuss architecture"
    When FollowerA reacts to post "Discuss architecture" with emoji "heart" via browser
    Then FollowerA should see reaction emoji "heart" on post "Discuss architecture"
    And Owner should see reaction emoji "heart" on post "Discuss architecture"
    And FollowerA should not see reaction emoji "thumbs_up" on post "Discuss architecture"
    And Owner should not see reaction emoji "thumbs_up" on post "Discuss architecture"
    When FollowerA reacts to post "Discuss architecture" with emoji "heart" via browser
    Then FollowerA should not see reaction count 1 on post "Discuss architecture"
    And Owner should not see reaction count 1 on post "Discuss architecture"
    And FollowerA should not see reaction emoji "heart" on post "Discuss architecture"
    And Owner should not see reaction emoji "heart" on post "Discuss architecture"

  @FEAT-087 @HS-E2E-087-ROLLBACK
  Scenario: Failed reaction submission rolls back optimistic state on public post
    Given Owner has created Open post "Rollback reaction post" via browser
    And FollowerA browser has approved FEAT-087 reaction circuit artifacts available
    And FollowerA will receive a rejected reaction submission response
    When FollowerA attempts reaction "thumbs_up" on post "Rollback reaction post" via browser
    Then FollowerA should see reaction error "Rejected by FEAT-087 rollback test" on post "Rollback reaction post"
    And FollowerA should not see reaction count 1 on post "Rollback reaction post"
    And FollowerA should not see reaction emoji "thumbs_up" on post "Rollback reaction post"

  @FEAT-087 @HS-E2E-087-DENIED-PERMALINK
  Scenario: Unauthorized private permalink hides reaction metadata
    Given Owner has approved followers "FollowerA" via browser
    And Owner creates Close post "Private architecture note" for Inner Circle via backend
    When FollowerB opens permalink for post "Private architecture note"
    Then FollowerB should see a generic access denied message
    And FollowerB should not see private reaction metadata on denied permalink

  @FEAT-087 @HS-E2E-087-PUBLIC-PERMALINK
  Scenario: Public permalink shows aggregate reaction state without private metadata
    Given Owner has created Open post "Permalink reaction signal" via browser
    And FollowerA browser has approved FEAT-087 reaction circuit artifacts available
    When FollowerA reacts to post "Permalink reaction signal" with emoji "thumbs_up" via browser
    And FollowerB opens permalink for post "Permalink reaction signal"
    Then FollowerB should see permalink post "Permalink reaction signal"
    And FollowerB should see reaction count 1 on permalink for post "Permalink reaction signal"
    And FollowerB should see reaction emoji "thumbs_up" on permalink for post "Permalink reaction signal"

  @FEAT-088 @ignore @HS-E2E-088-COMMENTS
  Scenario: Comments and single-level replies on authorized post
    Given Owner has created Open post "Discuss architecture" via browser
    When FollowerA reacts to post "Discuss architecture" with emoji "thumbs_up" via browser
    And FollowerA comments "Looks good" on post "Discuss architecture" via browser
    And Owner replies "Thanks for feedback" to comment "Looks good" via browser
    Then both users should see comment "Looks good"
    And both users should see reply "Thanks for feedback"
    And the reply action should be limited to single-level depth

  @FEAT-087 @FEAT-089 @HS-E2E-089-GUEST-CTA
  Scenario: Guest interaction opens account creation overlay on public post
    Given an unauthenticated browser session
    And Owner has created Open post "Guest visible content" via browser
    When guest attempts to react to post "Guest visible content" via browser
    Then guest should see account creation overlay
    When guest attempts to comment on post "Guest visible content" via browser
    Then guest should see account creation overlay

  @FEAT-090 @ignore @HS-E2E-090-TIMELINE
  Scenario: Follow graph drives following-first timeline
    Given FollowerA follows Owner via browser
    And Owner creates Open post "Update from owner" via browser
    And FollowerB creates Open post "Update from other user" via browser
    When FollowerA opens home timeline
    Then post "Update from owner" should appear before post "Update from other user"

  @FEAT-091 @ignore @HS-E2E-091-NOTIFICATIONS
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
