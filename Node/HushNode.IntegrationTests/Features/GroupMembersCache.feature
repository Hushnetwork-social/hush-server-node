@Integration
Feature: Group Members Cache with Display Names
  As a client displaying group members
  I want group members cached with display names
  So that GetGroupMembers doesn't make N+1 identity lookups

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed

  # Cache-Aside Pattern for Group Members
  @GroupMembersCache
  Scenario: Group members are cached with display names on first lookup
    Given Alice has created a group feed "MemberCacheTest"
    And the Redis group members cache for "MemberCacheTest" is empty
    When the group members for "MemberCacheTest" are looked up via gRPC
    Then the group members should be in the Redis cache for "MemberCacheTest"
    And the cached members should include display names

  @GroupMembersCache
  Scenario: Group members cache hit returns data without N identity lookups
    Given Alice has created a group feed "CacheHitTest"
    And the group members for "CacheHitTest" have been cached
    When the group members for "CacheHitTest" are looked up via gRPC again
    Then the response should contain group members with display names
