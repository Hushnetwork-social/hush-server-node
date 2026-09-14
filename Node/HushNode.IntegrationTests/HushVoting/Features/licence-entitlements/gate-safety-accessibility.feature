@HushVoting @HV-E2E @E2E @HV-LICENCE-ENTITLEMENTS @HV-FEAT-016
@FEAT-016
Feature: Entitlement gate safety and accessibility journeys
  FEAT-016 phase 7 canonical production-composition journeys for Lock/stale
  result rejection, incompatible projections, Back behavior, and assistive
  technology. Real composition and controlled real HushServerNode fixture.
  AT-LIC-012 explicitly corrupts a real Active server transport response to
  qualify rejection; it does not qualify a valid future catalogue release.

  @EPIC-002 @AT-LIC-011 @Playwright @server-fixture @HV-LIC-RECOVERY-MIGRATED
  Scenario: Lock prevents a late entitlement result from restoring access
    Given Alice is authenticated and entitlement resolution is in progress
    When Alice Locks HushVoting
    And the old operation completes later
    Then its stale epoch result is ignored
    And the workspace remains unmounted
    And no Alice entitlement is available to a later identity

  @EPIC-002 @AT-LIC-012 @Playwright @server-fixture @HV-LIC-COMPATIBILITY
  Scenario: Incompatible active projection cannot become Direct Free
    Given HushServerNode returns active entitlement with incompatible critical semantics
    When HushVoting validates the response
    Then workspace remains gated with compatible-client guidance
    And it is not mapped to Direct Free or known Veritas
    And no baseline transaction is created
    And an uncorrupted fresh node response restores the same indexed entitlement

  @EPIC-002 @AT-LIC-016-006 @Playwright @server-fixture @HV-LIC-RECOVERY-MIGRATED
  Scenario: Back cannot bypass or cancel the authenticated gate
    Given Alice is authenticated and waiting for indexed entitlement
    When Alice uses browser or platform Back
    Then HushVoting remains on the authenticated entitlement gate
    And it neither exposes workspace nor returns to pre-authentication UI

  @EPIC-002 @AT-LIC-016-007 @Playwright @server-fixture @HV-LIC-RECOVERY-MIGRATED
  Scenario: Entitlement recovery is accessible
    Given Alice uses keyboard screen-reader reduced-motion and enlarged text
    When states change from resolving through delayed or unavailable
    Then the gate announces meaningful changes without polling spam
    And Retry and Lock have visible focus names and deterministic focus placement
