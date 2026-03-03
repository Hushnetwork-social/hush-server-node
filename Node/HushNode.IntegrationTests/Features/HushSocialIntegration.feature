@Integration @HushSocial @EPIC008
Feature: HushSocial server integration rules
  As the HushSocial backend
  I want audience, privacy, interaction, and notification contracts enforced
  So that EPIC-008 behavior is deterministic and secure

  Background:
    Given a HushServerNode at block 5
    And user "Owner" is registered with a personal feed
    And user "FollowerA" is registered with a personal feed
    And user "FollowerB" is registered with a personal feed
    And user "FollowerC" is registered with a personal feed
    And user "GuestLikeUser" is not authenticated

  @FEAT-084 @FEAT-085 @FEAT-090
  Scenario: Close profile follow request acceptance auto-adds inner circle
    Given Owner has HushSocial enabled
    And Owner profile mode is Close
    When FollowerA requests to follow Owner
    Then the follow request should be pending approval
    When Owner accepts follow request from FollowerA
    Then FollowerA should be in Owner Inner Circle
    And Owner should see FollowerA in approved followers

  @FEAT-085
  Scenario: Circle membership removal rotates keys immediately
    Given Owner profile mode is Close
    And Owner has accepted follow requests from "FollowerA, FollowerB"
    And Owner has created circle "Dev Circle"
    And Owner has added "FollowerA, FollowerB" to circle "Dev Circle"
    And Owner posts "Initial private design note" to circle "Dev Circle"
    When Owner removes FollowerB from circle "Dev Circle"
    Then key generation for circle "Dev Circle" should be incremented
    And FollowerB should not be able to decrypt new posts in circle "Dev Circle"
    And FollowerA should be able to decrypt new posts in circle "Dev Circle"

  @FEAT-085
  Scenario: Startup bootstrap creates inner circle and links existing chat peers
    Given Owner profile mode is Close
    And Owner has existing chat feeds with "FollowerA, FollowerB"
    And Owner does not have an Inner Circle yet
    When Owner opens HushFeeds and personal feed bootstrap runs
    Then Owner Inner Circle should be created
    And "FollowerA, FollowerB" should be members of Owner Inner Circle

  @FEAT-085
  Scenario: New chat feed creation triggers add-members-to-inner-circle
    Given Owner profile mode is Close
    And Owner Inner Circle already exists
    And Owner starts a new chat feed with FollowerC
    When FEAT-085 background sync runs after chat creation
    Then FollowerC should be added to Owner Inner Circle
    And key generation for Owner Inner Circle should be incremented

  @FEAT-086
  Scenario: Open and close post visibility is enforced by permalink access
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has created an Open post "Public roadmap"
    And Owner has created a Close post "Inner circle roadmap" for Inner Circle
    When an unauthenticated user opens permalink for post "Public roadmap"
    Then the post content should be visible
    When FollowerC opens permalink for post "Inner circle roadmap"
    Then a generic denial message should be returned
    And no private metadata should be returned
    When FollowerA opens permalink for post "Inner circle roadmap"
    Then the post content should be visible

  @FEAT-086
  Scenario: Media constraints for social posts match HushFeeds limits
    Given Owner has HushSocial enabled
    When Owner submits an Open post with 5 valid media attachments
    Then the submission should be accepted
    When Owner submits an Open post with 6 media attachments
    Then the submission should be rejected with a count limit error
    When Owner submits an Open post with a media attachment over max size
    Then the submission should be rejected with a size limit error

  @FEAT-087
  Scenario: Reaction privacy preserves tally without exposing per-user emoji choice on private content
    Given Owner profile mode is Close
    And Owner has accepted follow requests from "FollowerA, FollowerB"
    And Owner has created a Close post "Private sentiment test" for Inner Circle
    When FollowerA reacts to "Private sentiment test" with "thumbs_up"
    And FollowerB reacts to "Private sentiment test" with "fire"
    Then authorized viewers should see reaction tally updates on "Private sentiment test"
    And the backend should not expose exact reaction choice per individual user to other viewers
    When FollowerA changes reaction on "Private sentiment test" to "heart"
    Then only one active reaction should exist for FollowerA on "Private sentiment test"

  @FEAT-088
  Scenario: Comments and single-level replies inherit post audience
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has created a Close post "Architecture thread" for Inner Circle
    When FollowerA comments "Looks good" on post "Architecture thread"
    And Owner replies "Thanks" to comment "Looks good"
    Then FollowerA should see comment "Looks good" and reply "Thanks"
    When FollowerC opens post "Architecture thread"
    Then FollowerC should receive access denied for post comments
    And FollowerC should receive access denied for post replies

  @FEAT-089
  Scenario: Guest users cannot interact with public content
    Given Owner has created an Open post "Public onboarding post"
    When GuestLikeUser attempts to react to post "Public onboarding post"
    Then the action should be rejected as unauthenticated
    When GuestLikeUser attempts to comment on post "Public onboarding post"
    Then the action should be rejected as unauthenticated

  @FEAT-090
  Scenario: Following-first timeline prioritization for authenticated users
    Given Owner has accepted follow request from FollowerA
    And FollowerA follows Owner
    And FollowerB does not follow Owner
    And Owner has created Open post "From followed account"
    And FollowerB has created Open post "From non-followed account"
    When FollowerA requests home timeline
    Then posts from followed accounts should be prioritized before non-followed posts

  @FEAT-091
  Scenario: Notification delivery honors eligibility and privacy-safe payloads
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has not accepted follow request from FollowerC
    And FollowerA has enabled Close post notifications
    And FollowerA has not muted circle "Inner Circle"
    When Owner publishes Close post "Security update" to Inner Circle
    Then FollowerA should receive in-app notification for "Security update"
    And FollowerA should receive push notification with no content preview
    And FollowerC should not receive any notification for "Security update"

  @FEAT-091
  Scenario: Per-circle mute suppresses notifications for muted circle
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has created circle "Trading Circle"
    And Owner has added FollowerA to circle "Trading Circle"
    And FollowerA has muted circle "Trading Circle"
    When Owner publishes Close post "Trade signal" to circle "Trading Circle"
    Then FollowerA should not receive in-app notification for "Trade signal"
    And FollowerA should not receive push notification for "Trade signal"
