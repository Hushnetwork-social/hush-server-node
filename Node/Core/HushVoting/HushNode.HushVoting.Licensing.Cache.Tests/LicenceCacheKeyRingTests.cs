using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Key-ring validation and HKDF domain-separation tests (Task 2.5/2.6). Uses fixed public test-only
/// key material; never production secrets.
/// </summary>
public sealed class LicenceCacheKeyRingTests
{
    private static readonly LicenceCacheOptions Options = new();

    private static byte[] KeyBytes(int seed) => Enumerable.Range(0, 32).Select(i => (byte)(seed + i)).ToArray();

    private static LicenceCacheMasterKey Master(string id, int seed, DateTime rotationUtc) =>
        LicenceCacheMasterKey.Create(id, KeyBytes(seed), rotationUtc, Options, out _);

    [Fact]
    public void Valid_current_only_ring_is_accepted()
    {
        var ring = LicenceCacheKeyRing.TryCreate(
            Master("v1", 1, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc)),
            previous: null,
            Options,
            out var code);

        ring.Should().NotBeNull();
        code.Should().BeNull();
        ring!.HasPrevious.Should().BeFalse();
    }

    [Fact]
    public void Current_and_previous_within_overlap_are_accepted()
    {
        var current = Master("v2", 10, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));
        var previous = Master("v1", 1, new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc)); // 11 days earlier

        var ring = LicenceCacheKeyRing.TryCreate(current, previous, Options, out var code);
        ring.Should().NotBeNull();
        code.Should().BeNull();
        ring!.HasPrevious.Should().BeTrue();
    }

    [Fact]
    public void Duplicate_key_ids_are_rejected()
    {
        var now = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        var ring = LicenceCacheKeyRing.TryCreate(
            Master("v1", 1, now),
            Master("v1", 2, now.AddDays(-1)),
            Options,
            out var code);

        ring.Should().BeNull();
        code.Should().Be(LicenceCacheReasonCodes.DuplicateKeyId);
    }

    [Fact]
    public void Previous_must_strictly_precede_current()
    {
        var now = new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc);
        var ring = LicenceCacheKeyRing.TryCreate(
            Master("v2", 10, now),
            Master("v1", 1, now),
            Options,
            out var code);

        ring.Should().BeNull();
        code.Should().Be(LicenceCacheReasonCodes.PreviousNotOlder);
    }

    [Fact]
    public void Overlap_beyond_fourteen_days_is_rejected()
    {
        var current = Master("v2", 10, new DateTime(2026, 9, 5, 0, 0, 0, DateTimeKind.Utc));
        var previous = Master("v1", 1, new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc)); // 35 days earlier

        var ring = LicenceCacheKeyRing.TryCreate(current, previous, Options, out var code);
        ring.Should().BeNull();
        code.Should().Be(LicenceCacheReasonCodes.OverlapExceedsLimit);
    }

    [Fact]
    public void Weak_master_key_entropy_is_rejected()
    {
        var act = () => LicenceCacheMasterKey.Create(
            "v1",
            new byte[16],
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            Options,
            out _);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Invalid_or_oversized_key_id_is_rejected()
    {
        var now = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);
        var act1 = () => LicenceCacheMasterKey.Create("", KeyBytes(1), now, Options, out _);
        act1.Should().Throw<ArgumentException>();

        var act2 = () => LicenceCacheMasterKey.Create(new string('k', 65), KeyBytes(1), now, Options, out _);
        act2.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Hkdf_derives_two_different_stable_domain_separated_subkeys()
    {
        var master = KeyBytes(42);

        var subjectKey1 = LicenceCacheKeyDerivation.DeriveSubjectKey(master);
        var authKey1 = LicenceCacheKeyDerivation.DeriveValueAuthenticationKey(master);
        var subjectKey2 = LicenceCacheKeyDerivation.DeriveSubjectKey(master);

        subjectKey1.Should().HaveCount(32);
        authKey1.Should().HaveCount(32);
        subjectKey1.Should().NotEqual(authKey1);          // purpose separation
        subjectKey1.Should().Equal(subjectKey2);          // deterministic
        subjectKey1.Should().NotEqual(master);            // master never used directly

        // Fixed public test vectors lock the derivation (do not change context labels silently).
        LicenceCacheKeyDerivation.SubjectKeyContext.Should().Be("hushvoting/licence-cache/v1/subject-key");
        LicenceCacheKeyDerivation.ValueAuthenticationContext.Should()
            .Be("hushvoting/licence-cache/v1/value-authentication");
    }

    [Fact]
    public void Subject_digest_is_stable_and_address_sensitive()
    {
        var subjectKey = LicenceCacheKeyDerivation.DeriveSubjectKey(KeyBytes(7));

        var digestA = LicenceCacheKeyDerivation.ComputeSubjectDigest(subjectKey, "NXc1...address-A");
        var digestB = LicenceCacheKeyDerivation.ComputeSubjectDigest(subjectKey, "NXc1...address-A");
        var digestC = LicenceCacheKeyDerivation.ComputeSubjectDigest(subjectKey, "NXc1...address-B");

        digestA.Should().HaveCount(32);
        digestA.Should().Equal(digestB);
        digestA.Should().NotEqual(digestC);

        // A different master produces a different digest for the same address.
        var otherKey = LicenceCacheKeyDerivation.DeriveSubjectKey(KeyBytes(8));
        LicenceCacheKeyDerivation.ComputeSubjectDigest(otherKey, "NXc1...address-A")
            .Should().NotEqual(digestA);
    }
}
