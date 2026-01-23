@Integration
Feature: FEAT-050 Feed Participants & Group Keys Cache
  As the notification handler
  I want feed participants and group keys cached
  So that notifications don't block on database queries

  Background:
    Given a HushServerNode at block 1
    And Redis cache is available
    And user "Alice" is registered with a personal feed
    And user "Bob" is registered with a personal feed

  # Key Generations Cache - Cache-Aside Pattern (populated via GetKeyGenerations gRPC)
  # This is the testable integration path - gRPC handler populates cache synchronously
  @FEAT-050 @KeyGenerations
  Scenario: Group key generations are cached on first lookup
    Given Alice has created a group feed "KeyTestGroup"
    And the Redis key generations cache for "KeyTestGroup" is empty
    When the key generations for "KeyTestGroup" are looked up via gRPC
    Then the key generations should be in the Redis cache for "KeyTestGroup"
    And the cache should contain at least one key generation

  @FEAT-050 @KeyGenerations
  Scenario: Key generations cache hit returns data without database query
    Given Alice has created a group feed "CacheHitGroup"
    And the key generations for "CacheHitGroup" have been cached
    When the key generations for "CacheHitGroup" are looked up via gRPC again
    Then the response should contain the key generations

  # Fallback Behavior - Key Generations
  @FEAT-050 @Fallback
  Scenario: Key generations lookup works after cache flush
    Given Alice has created a group feed "KeyFallbackGroup"
    And the group has key generations in the database
    When the Redis key generations cache for "KeyFallbackGroup" is flushed
    And the key generations for "KeyFallbackGroup" are looked up via gRPC
    Then the response should contain the key generations
    And the key generations should be in the Redis cache for "KeyFallbackGroup"

  # Participants Cache Service Exists and Is Functional
  # Note: Participants cache is populated by NotificationEventHandler via async events
  # which can't be reliably tested in integration tests due to fire-and-forget timing.
  # Unit tests in NotificationEventHandlerTests.cs cover the cache population logic.
  @FEAT-050 @ParticipantsService
  Scenario: Feed participants cache service is registered and functional
    Given Alice has created a group feed "ServiceTestGroup"
    When the participants cache service stores participants for "ServiceTestGroup"
    Then the participants should be retrievable from the cache service

  # Key Generations Cache Invalidation on Join - Bug Fix Test
  # When a user joins a group, the key generations cache must be invalidated
  # so that subsequent queries return the new key generation with the joining user's key
  @FEAT-050 @KeyGenerations @JoinGroup
  Scenario: Joining user receives key generations after joining public group
    Given Alice has created a public group feed "JoinKeyTestGroup"
    And the key generations for "JoinKeyTestGroup" have been cached
    When Bob joins the public group "JoinKeyTestGroup" via gRPC
    And Bob looks up key generations for "JoinKeyTestGroup" via gRPC
    Then Bob should receive at least one key generation for "JoinKeyTestGroup"

  # CRITICAL: This test verifies that existing members (Alice) receive new KeyGenerations
  # after a new member (Bob) joins. This is essential for Alice to decrypt messages
  # sent by Bob (which are encrypted with the NEW key generation).
  #
  # Note: ValidToBlock is NOT tracked/returned by the server. Messages include their
  # KeyGeneration number, so clients know which key to use. ValidToBlock would be
  # useful for validation/auditing but is not required for functionality.
  # See: MemoryBank/Architecture/CLIENT_ARCHITECTURE.md - "Group Feed Key Management"
  @FEAT-050 @KeyGenerations @JoinGroup @Critical
  Scenario: Existing member receives new key generation after another user joins
    Given Alice has created a public group feed "ExistingMemberKeyGroup"
    # At this point: KeyGen 0 exists with Alice's encrypted key
    And Alice looks up key generations for "ExistingMemberKeyGroup" via gRPC
    Then Alice should receive exactly 1 key generation for "ExistingMemberKeyGroup"
    # Bob joins the group
    When Bob joins the public group "ExistingMemberKeyGroup" via gRPC
    # After join: KeyGen 1 should be created with encrypted keys for BOTH Alice and Bob
    And Alice looks up key generations for "ExistingMemberKeyGroup" via gRPC
    Then Alice should receive exactly 2 key generations for "ExistingMemberKeyGroup"
    And the database should have 2 key generations for "ExistingMemberKeyGroup"
    And Alice should have an encrypted key for KeyGeneration 1 in "ExistingMemberKeyGroup"
    And Bob should have an encrypted key for KeyGeneration 1 in "ExistingMemberKeyGroup"

  # CRITICAL: This test verifies that messages include the correct KeyGeneration number.
  # Messages MUST track which key was used for encryption so clients know which key to use
  # for decryption. This is especially important after membership changes trigger key rotation.
  @FEAT-050 @KeyGenerations @Messages @Critical
  Scenario: Messages are encrypted with the correct key generation before and after member joins
    Given Alice has created a public group feed "MessageKeyGenGroup"
    # Alice sends a message BEFORE Bob joins - should use KeyGen 0
    When Alice sends a group message "Hello before Bob!" to "MessageKeyGenGroup" with KeyGeneration 0
    Then the message "Hello before Bob!" in "MessageKeyGenGroup" should have KeyGeneration 0
    # Bob joins the group - triggers key rotation to KeyGen 1
    When Bob joins the public group "MessageKeyGenGroup" via gRPC
    And Alice looks up key generations for "MessageKeyGenGroup" via gRPC
    Then Alice should receive exactly 2 key generations for "MessageKeyGenGroup"
    # Alice sends a message AFTER Bob joins - should use KeyGen 1
    When Alice sends a group message "Hello after Bob!" to "MessageKeyGenGroup" with KeyGeneration 1
    Then the message "Hello after Bob!" in "MessageKeyGenGroup" should have KeyGeneration 1
    # Bob can also send with KeyGen 1
    When Bob sends a group message "Hi from Bob!" to "MessageKeyGenGroup" with KeyGeneration 1
    Then the message "Hi from Bob!" in "MessageKeyGenGroup" should have KeyGeneration 1

  # CRITICAL TWIN TEST: This mirrors the Playwright E2E test flow exactly.
  # Tests the COMPLETE flow: key rotation -> key sync -> key decryption -> message decryption
  # This is the "server-side twin" of the browser E2E test where Alice couldn't decrypt Bob's messages.
  #
  # The browser flow is:
  # 1. Alice creates group (localStorage: KeyGen 0 with decrypted AES key)
  # 2. Bob joins (server creates KeyGen 1)
  # 3. Alice's 3s-sync fetches KeyGen 1 (localStorage updated with encrypted AES key)
  # 4. Alice decrypts KeyGen 1's AES key using her private key
  # 5. Bob sends message encrypted with KeyGen 1
  # 6. Alice decrypts Bob's message using KeyGen 1's AES key
  #
  # If this test passes but the browser test fails, the issue is in the client-side sync/storage.
  # If this test fails, the issue is in the server-side key rotation/distribution.
  @FEAT-050 @KeyGenerations @Decryption @TwinTest @Critical
  Scenario: Alice can decrypt Bob's message after key rotation (Twin Test for E2E)
    # Step 1: Alice creates group - she has KeyGen 0
    Given Alice has created a public group feed "DecryptionTestGroup"
    And Alice looks up key generations for "DecryptionTestGroup" via gRPC
    Then Alice should receive exactly 1 key generation for "DecryptionTestGroup"
    # Verify Alice can decrypt KeyGen 0's AES key
    When Alice decrypts her AES key for KeyGeneration 0 in "DecryptionTestGroup"
    Then the decryption should succeed for Alice

    # Step 2: Bob joins - server creates KeyGen 1 with keys for both Alice and Bob
    When Bob joins the public group "DecryptionTestGroup" via gRPC

    # Step 3: Alice syncs and receives KeyGen 1 (simulates 3s-sync updating localStorage)
    When Alice looks up key generations for "DecryptionTestGroup" via gRPC
    Then Alice should receive exactly 2 key generations for "DecryptionTestGroup"
    And Alice should have an encrypted key for KeyGeneration 1 in "DecryptionTestGroup"

    # Step 4: Alice decrypts KeyGen 1's AES key using her private encrypt key
    When Alice decrypts her AES key for KeyGeneration 1 in "DecryptionTestGroup"
    Then the decryption should succeed for Alice

    # Step 5: Bob sends a message encrypted with KeyGen 1
    # First, Bob needs to get and decrypt his copy of KeyGen 1
    When Bob looks up key generations for "DecryptionTestGroup" via gRPC
    Then Bob should receive exactly 1 key generation for "DecryptionTestGroup"
    When Bob decrypts his AES key for KeyGeneration 1 in "DecryptionTestGroup"
    Then the decryption should succeed for Bob
    # Now Bob sends his message
    When Bob sends a group message "Hello Alice, I joined!" to "DecryptionTestGroup" using his decrypted KeyGeneration 1 key

    # Step 6: Alice decrypts Bob's message using her KeyGen 1 AES key
    Then Alice should be able to decrypt Bob's message "Hello Alice, I joined!" in "DecryptionTestGroup"
