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

  @FEAT-084 @FEAT-085 @FEAT-090 @HS-INT-085-ONBOARDING
  Scenario: Close profile follow request acceptance auto-adds inner circle
    Given Owner has HushSocial enabled
    And Owner profile mode is Close
    When FollowerA requests to follow Owner
    Then the follow request should be pending approval
    When Owner accepts follow request from FollowerA
    Then FollowerA should be in Owner Inner Circle
    And Owner should see FollowerA in approved followers

  @FEAT-085 @HS-INT-085-CIRCLE-REMOVAL
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

  @FEAT-085 @HS-INT-085-BOOTSTRAP-LINK
  Scenario: Startup bootstrap creates inner circle and links existing chat peers
    Given Owner profile mode is Close
    And Owner has existing chat feeds with "FollowerA, FollowerB"
    And Owner does not have an Inner Circle yet
    When Owner opens HushFeeds and personal feed bootstrap runs
    Then Owner Inner Circle should be created
    And "FollowerA, FollowerB" should be members of Owner Inner Circle

  @FEAT-085 @HS-INT-085-CHAT-ADD
  Scenario: New chat feed creation triggers add-members-to-inner-circle
    Given Owner profile mode is Close
    And Owner Inner Circle already exists
    And Owner starts a new chat feed with FollowerC
    When FEAT-085 background sync runs after chat creation
    Then FollowerC should be added to Owner Inner Circle
    And key generation for Owner Inner Circle should be incremented

  @FEAT-085 @HS-INT-085-DUPLICATE-ADD
  Scenario: Duplicate inner-circle add request returns explicit duplicate without key rotation
    Given Owner profile mode is Close
    And Owner Inner Circle already exists
    And Owner has accepted follow request from FollowerA
    When Owner tries to add "FollowerA" again to Owner Inner Circle
    Then FEAT-085 duplicate add response should include "FollowerA"
    And key generation for Owner Inner Circle should remain unchanged

  @FEAT-085 @HS-INT-085-SAME-BLOCK-DUP
  Scenario: Same-block duplicate add-member requests remain deterministic
    Given Owner profile mode is Close
    And Owner Inner Circle already exists
    When Owner submits duplicate add-members requests for "FollowerA" before block indexing
    And a block is produced
    Then "FollowerA" should be added to Owner Inner Circle
    And FEAT-085 same-block duplicate processing should rotate Owner Inner Circle only once

  @FEAT-085 @HS-INT-085-SAME-BLOCK-CREATE
  Scenario Outline: Same-block create-inner-circle for multiple owners is isolated
    Given <ownerCount> FEAT-085 owners are registered with personal feeds
    When all FEAT-085 owners submit CreateInnerCircle before block indexing
    And a block is produced
    Then all FEAT-085 owners should have exactly one Inner Circle
    And all FEAT-085 create responses should be accepted pre-indexing

    Examples:
      | ownerCount |
      | 2          |
      | 5          |
      | 10         |

  @FEAT-086 @HS-INT-086-VISIBILITY
  Scenario: Open and close post visibility is enforced by permalink access
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has created an Open post "Public roadmap"
    And Owner has created a Close post "Inner circle roadmap" for Inner Circle
    When an unauthenticated user opens permalink for post "Public roadmap"
    Then the post content should be visible
    When FollowerC opens permalink for post "Inner circle roadmap"
    Then a generic denial message should be returned
    And the permalink denial contract should request access from owner
    And no private metadata should be returned
    When FollowerA opens permalink for post "Inner circle roadmap"
    Then the post content should be visible

  @FEAT-086 @HS-INT-086-COMPOSER
  Scenario: Composer contract defaults private mode to Inner Circle with last-circle lock
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner Inner Circle already exists
    When Owner requests social composer contract in private mode
    Then social composer default visibility should be private
    And social composer should select Owner Inner Circle by default
    And social composer should lock the last selected private circle
    And social composer submit should be allowed

  @FEAT-086 @HS-INT-086-GUEST-DENIAL
  Scenario: Guest private permalink denial returns create-account CTA contract
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has created a Close post "Guest denied private post" for Inner Circle
    When an unauthenticated user opens permalink for post "Guest denied private post"
    Then the permalink denial contract should target guest account creation
    And no private metadata should be returned

  @FEAT-086 @HS-INT-086-MEDIA
  Scenario: Media constraints for social posts match HushFeeds limits
    Given Owner has HushSocial enabled
    When Owner submits an Open post with 5 valid media attachments
    Then the submission should be accepted
    When Owner submits an Open post with 6 media attachments
    Then the submission should be rejected with a count limit error
    When Owner submits an Open post with a media attachment over max size
    Then the submission should be rejected with a size limit error

  @FEAT-087 @HS-INT-087-REACTION-PRIVACY
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

  @FEAT-088 @HS-INT-088-COMMENTS
  Scenario: Comments and single-level replies inherit post audience
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has created a Close post "Architecture thread" for Inner Circle
    When FollowerA comments "Looks good" on post "Architecture thread"
    And Owner replies "Thanks" to comment "Looks good"
    And Owner reacts to comment "Looks good" with "thumbs_up"
    And FollowerA reacts to reply "Thanks" with "heart"
    Then FollowerA should see comment "Looks good" and reply "Thanks"
    And authorized viewers should see reaction tally updates on comment "Looks good"
    And authorized viewers should see reaction tally updates on reply "Thanks"
    When FollowerC opens post "Architecture thread"
    Then FollowerC should receive access denied for post comments
    And FollowerC should receive access denied for post replies

  @FEAT-089 @HS-INT-089-GUEST-CTA
  Scenario: Guest interaction contracts require auth while preserving public readability
    Given Owner has created an Open post "Public onboarding post"
    When an unauthenticated user opens permalink for post "Public onboarding post"
    Then the post content should be visible
    And the public permalink should remain read-only for guests
    Given Owner profile mode is Close
    And Owner has created a Close post "Guest gated post" for Inner Circle
    When an unauthenticated user opens permalink for post "Guest gated post"
    Then the permalink denial contract should target guest account creation

  @FEAT-090 @ignore
  Scenario: Following-first timeline prioritization for authenticated users
    Given Owner has accepted follow request from FollowerA
    And FollowerA follows Owner
    And FollowerB does not follow Owner
    And Owner has created Open post "From followed account"
    And FollowerB has created Open post "From non-followed account"
    When FollowerA requests home timeline
    Then posts from followed accounts should be prioritized before non-followed posts

  @FEAT-091 @ignore
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

  @FEAT-091 @ignore
  Scenario: Per-circle mute suppresses notifications for muted circle
    Given Owner profile mode is Close
    And Owner has accepted follow request from FollowerA
    And Owner has created circle "Trading Circle"
    And Owner has added FollowerA to circle "Trading Circle"
    And FollowerA has muted circle "Trading Circle"
    When Owner publishes Close post "Trade signal" to circle "Trading Circle"
    Then FollowerA should not receive in-app notification for "Trade signal"
    And FollowerA should not receive push notification for "Trade signal"
