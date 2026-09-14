using System.Reflection;
using FluentAssertions;
using Xunit;

namespace HushNode.HushVoting.Licensing.Cache.Tests;

/// <summary>
/// FEAT-015 read-surface guard: the licence display/read path exposes no assignment mutation
/// command. Cached reads must never be able to provision, activate, expire, or assign — Redis and
/// the cached reader remain display-only while block indexing is the only activation authority.
/// </summary>
public sealed class LicenceReadSurfaceArchitectureTests
{
    [Fact]
    public void Cached_reader_contract_exposes_no_direct_write_command()
    {
        var readerType = typeof(ICachedEntitlementReader);
        var readMethodNames = readerType.GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Select(method => method.Name)
            .ToArray();

        readMethodNames.Should().Contain("GetEffectiveEntitlementAsync");
        readMethodNames.Should().NotContain(
            new[] { "ProvisionAsync", "ActivateAsync", "ExpireAsync", "AssignAsync" },
            "the cached read path must never expose assignment mutation commands");
    }
}
