using System.Reflection;
using FluentAssertions;
using HushNode.HushVoting.Licensing.Cache;
using HushNode.HushVoting.Licensing.Storage;
using HushServerNode.HushVotingLicensingIntegration;
using Xunit;

namespace HushServerNode.Tests.HushVotingLicence;

/// <summary>
/// FEAT-015 licence serving architecture guard (RED first): the display/query authority resolver
/// may never fall back to the FEAT-013 direct-write service (GetOrProvision provisions Direct Free
/// and activates higher plans on read). Serving reads must resolve the indexed projection only and
/// return explicit no-active plus a Direct Free template when no assignment is effective.
/// </summary>
public sealed class LicenceServingArchitectureTests
{
    [Fact]
    public void Serving_authority_resolver_never_depends_on_the_direct_write_service()
    {
        var hostAssembly = typeof(HushVotingLicenceCacheHostBuild).Assembly;
        var resolverInterface = typeof(IEntitlementAuthorityResolver);
        var directWriteService = typeof(LicenceEntitlementService);
        var serviceFactoryType = typeof(Func<>).MakeGenericType(directWriteService);

        var violations = hostAssembly
            .GetTypes()
            .Where(type => resolverInterface.IsAssignableFrom(type))
            .Where(type => type is { IsAbstract: false, IsInterface: false })
            .SelectMany(type => type.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            .SelectMany(constructor => constructor.GetParameters())
            .Where(parameter => parameter.ParameterType == directWriteService
                                || parameter.ParameterType == serviceFactoryType)
            .Select(parameter => $"{parameter.Member.DeclaringType!.Name}..ctor({parameter.ParameterType.Name})")
            .Distinct()
            .OrderBy(entry => entry)
            .ToArray();

        violations.Should().BeEmpty(
            "serving licence reads must resolve the indexed projection and must never originate a " +
            "Direct Free provisioning or higher-plan activation write");
    }
}
