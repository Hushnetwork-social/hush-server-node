using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// FEAT-014 Phase 6 architecture boundaries (Tasks 6.5/6.6). The licence display cache is a
/// read-optimization only: activation and enforcement live in the authoritative FEAT-013 module and
/// must never reference the cached reader or cached projection. The cache module itself must not
/// reference transport, client, or a second Redis connection owner. These rules are machine-enforced
/// so downstream FEAT-015/018 work cannot silently depend on cached data as authority.
/// </summary>
public sealed class LicenceCacheArchitectureBoundaryTests
{
    [Fact]
    public void Authority_module_never_references_the_display_cache()
    {
        // FEAT-013 authority (storage module) owns activation and enforcement semantics. It must not
        // reference the FEAT-014 cache module: cached data can never authorise activation/enforcement.
        var storageAssembly = typeof(LicenceEntitlementCoordinator).Assembly;
        var referenceNames = storageAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenceNames.Should().NotContain("HushNode.HushVoting.Licensing.Cache");
        referenceNames.Should().NotContain("HushNode.Caching");
        referenceNames.Should().NotContain("StackExchange.Redis");
    }

    [Fact]
    public void Cache_module_does_not_create_a_second_redis_connection_owner()
    {
        // The cache module consumes the shared multiplexer (StackExchange.Redis interface) but must
        // not reference the Notifications connection manager or any transport/client assembly.
        var cacheAssembly = typeof(ICachedEntitlementReader).Assembly;
        var referenceNames = cacheAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenceNames.Should().Contain("HushNode.HushVoting.Licensing.Storage");
        referenceNames.Should().Contain("StackExchange.Redis");
        referenceNames.Should().NotContain("HushNode.Notifications");
        referenceNames.Should().NotContain("Grpc.AspNetCore");
        referenceNames.Should().NotContain("Grpc.Net.Client");
    }

    [Fact]
    public void Cached_projection_and_reader_expose_no_authority_conversion()
    {
        // CachedEntitlementProjection can never become EffectiveLicenceEntitlement (authority).
        var projection = typeof(CachedEntitlementProjection);
        var authoritative = typeof(EffectiveLicenceEntitlement);

        projection.GetMethods().Where(m =>
            m.ReturnType == authoritative ||
            (m.ReturnType.Namespace ?? string.Empty)
                .StartsWith(authoritative.Namespace ?? string.Empty, StringComparison.Ordinal))
            .Should().BeEmpty();

        projection.GetMethods().Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Should().BeEmpty();

        // ICachedEntitlementReader only returns the bounded non-authoritative result.
        var reader = typeof(ICachedEntitlementReader);
        reader.GetMethod(nameof(ICachedEntitlementReader.GetEffectiveEntitlementAsync))!
            .ReturnType.Should().Be(typeof(Task<CachedEntitlementReadResult>));
    }

    [Fact]
    public void No_public_api_transport_or_client_contract_is_introduced()
    {
        // FEAT-014 introduces no gRPC/public/protobuf surface: nothing in the cache module may
        // reference gRPC or web transport.
        var cacheAssembly = typeof(ICachedEntitlementReader).Assembly;
        var publicTypes = cacheAssembly.GetExportedTypes().Select(t => t.FullName).ToArray();

        publicTypes.Should().NotContain(t => t != null && t.Contains("Grpc", StringComparison.Ordinal));
        publicTypes.Should().NotContain(t => t != null && t.Contains("Client", StringComparison.Ordinal));
    }
}
