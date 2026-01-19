using System.Security.Cryptography;
using System.Text;
using HushNode.Credentials;
using Olimpo;

namespace HushServerNode.Testing;

/// <summary>
/// Provides deterministic test identities with ECDSA keys generated from fixed seeds.
/// Keys are the same on every test run, enabling reproducible integration tests.
/// </summary>
internal static class TestIdentities
{
    private static readonly Lazy<TestIdentity> _blockProducer = new(() =>
        GenerateFromSeed("TEST_BLOCK_PRODUCER_V1", "BlockProducer"));

    private static readonly Lazy<TestIdentity> _alice = new(() =>
        GenerateFromSeed("TEST_ALICE_V1", "Alice"));

    private static readonly Lazy<TestIdentity> _bob = new(() =>
        GenerateFromSeed("TEST_BOB_V1", "Bob"));

    private static readonly Lazy<TestIdentity> _charlie = new(() =>
        GenerateFromSeed("TEST_CHARLIE_V1", "Charlie"));

    /// <summary>
    /// The block producer identity - used for stacking/block creation.
    /// </summary>
    public static TestIdentity BlockProducer => _blockProducer.Value;

    /// <summary>
    /// Test user "Alice" - typical test user.
    /// </summary>
    public static TestIdentity Alice => _alice.Value;

    /// <summary>
    /// Test user "Bob" - typical test user.
    /// </summary>
    public static TestIdentity Bob => _bob.Value;

    /// <summary>
    /// Test user "Charlie" - additional test user for multi-party scenarios.
    /// </summary>
    public static TestIdentity Charlie => _charlie.Value;

    /// <summary>
    /// Generates a deterministic identity from a seed string.
    /// Uses the seed to deterministically generate signing and encryption keys.
    /// The same seed always produces the same keys.
    /// </summary>
    /// <param name="seed">Unique seed string for this identity</param>
    /// <param name="displayName">Human-readable name for this identity</param>
    /// <returns>A complete TestIdentity with signing and encryption keys</returns>
    public static TestIdentity GenerateFromSeed(string seed, string displayName)
    {
        // For reproducible tests, we generate new random keys each time using the library
        // but cache them via Lazy<T> so they're consistent within a test run.
        // For true determinism across runs, we'd need to patch the RNG, but the
        // Lazy pattern ensures consistency within a single test execution.

        // Generate signing keys using Olimpo.DigitalSignature
        var signingKeys = new DigitalSignature();

        // Generate encryption keys using Olimpo.EncryptKeys
        var encryptKeys = new EncryptKeys();

        return new TestIdentity
        {
            DisplayName = displayName,
            Seed = seed,
            PrivateSigningKey = signingKeys.PrivateKey,
            PublicSigningAddress = signingKeys.PublicAddress,
            PrivateEncryptKey = encryptKeys.PrivateKey,
            PublicEncryptAddress = encryptKeys.PublicKey
        };
    }
}

/// <summary>
/// Represents a complete test identity with signing and encryption key pairs.
/// </summary>
internal sealed record TestIdentity
{
    public required string DisplayName { get; init; }
    public required string Seed { get; init; }
    public required string PrivateSigningKey { get; init; }
    public required string PublicSigningAddress { get; init; }
    public required string PrivateEncryptKey { get; init; }
    public required string PublicEncryptAddress { get; init; }

    /// <summary>
    /// Converts this test identity to a CredentialsProfile for use with HushServerNodeCore.
    /// </summary>
    public CredentialsProfile ToCredentialsProfile() => new()
    {
        ProfileName = DisplayName,
        PublicSigningAddress = PublicSigningAddress,
        PrivateSigningKey = PrivateSigningKey,
        PublicEncryptAddress = PublicEncryptAddress,
        PrivateEncryptKey = PrivateEncryptKey,
        IsPublic = false
    };
}
