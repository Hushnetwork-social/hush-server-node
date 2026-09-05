using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// Architecture-surface tests for the cache module boundary (Task 2.1). The module exposes a closed
/// public surface and must not depend on transport, web, or client assemblies. Activation/enforcement
/// consumer boundaries are additionally guarded in Phase 6 by the feature's focused architecture gate.
/// </summary>
public sealed class LicenceCacheArchitectureTests
{
    [Fact]
    public void Cache_assembly_public_surface_is_closed()
    {
        var cacheAssembly = typeof(ICachedEntitlementReader).Assembly;

        var publicTypes = cacheAssembly.GetExportedTypes()
            .Select(t => t.FullName!)
            .OrderBy(n => n)
            .ToArray();

        publicTypes.Should().BeEquivalentTo(new[]
        {
            "HushNode.HushVoting.Licensing.Cache.CachedEntitlementProjection",
            "HushNode.HushVoting.Licensing.Cache.CachedEntitlementReadResult",
            "HushNode.HushVoting.Licensing.Cache.EntitlementCacheReadOutcome",
            "HushNode.HushVoting.Licensing.Cache.ICachedEntitlementReader",
            "HushNode.HushVoting.Licensing.Cache.LicenceCacheOptionErrorCodes",
            "HushNode.HushVoting.Licensing.Cache.LicenceCacheOptions",
        });
    }

    [Fact]
    public void Cache_assembly_does_not_depend_on_transport_or_client_stack()
    {
        var cacheAssembly = typeof(ICachedEntitlementReader).Assembly;
        var referenceNames = cacheAssembly.GetReferencedAssemblies()
            .Select(a => a.Name)
            .ToArray();

        referenceNames.Should().Contain("HushNode.HushVoting.Licensing.Storage");
        referenceNames.Should().NotContain("Grpc.AspNetCore");
        referenceNames.Should().NotContain("Grpc.Net.Client");
        referenceNames.Should().NotContain("HushWeb");
        referenceNames.Should().NotContain("HushVotingWebClient");
    }

    [Fact]
    public void Reader_contract_returns_only_bounded_non_authoritative_result()
    {
        var readerType = typeof(ICachedEntitlementReader);
        var method = readerType.GetMethod(nameof(ICachedEntitlementReader.GetEffectiveEntitlementAsync));

        method.Should().NotBeNull();
        method!.ReturnType.Should().Be(typeof(Task<CachedEntitlementReadResult>));
        method.GetParameters().Select(p => p.ParameterType.Name)
            .Should().Contain("AuthenticatedIdentitySubject");
    }

    [Fact]
    public void Projection_has_no_mapping_back_to_authority()
    {
        // The only sanctioned mapping is authoritative -> cached. There must be no public method or
        // conversion on the projection that produces or exposes an authoritative entitlement type.
        const string authorityNamespace = "HushNode.HushVoting.Licensing.Storage";
        var projectionType = typeof(CachedEntitlementProjection);
        var authoritative = typeof(HushNode.HushVoting.Licensing.Storage.EffectiveLicenceEntitlement);

        var leaks = projectionType.GetMethods()
            .Where(m => m.ReturnType == authoritative ||
                        (m.ReturnType.Namespace ?? string.Empty).StartsWith(authorityNamespace,
                            StringComparison.Ordinal))
            .ToArray();
        leaks.Should().BeEmpty();

        // No explicit/implicit conversion operators toward authority.
        projectionType.GetMethods().Where(m => m.Name is "op_Implicit" or "op_Explicit")
            .Should().BeEmpty();
    }
}
