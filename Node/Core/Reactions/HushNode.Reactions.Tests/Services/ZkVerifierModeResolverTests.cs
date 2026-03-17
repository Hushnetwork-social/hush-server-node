using FluentAssertions;
using HushNode.Reactions.ZK;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace HushNode.Reactions.Tests.Services;

public class ZkVerifierModeResolverTests
{
    [Fact]
    public void Resolve_returns_real_when_explicitly_configured()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Reactions:VerifierMode"] = "real"
        });

        ZkVerifierModeResolver.Resolve(config).Should().Be(ZkVerifierMode.Real);
    }

    [Fact]
    public void Resolve_returns_dev_when_explicitly_configured()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Reactions:VerifierMode"] = "dev"
        });

        ZkVerifierModeResolver.Resolve(config).Should().Be(ZkVerifierMode.Dev);
    }

    [Fact]
    public void Resolve_falls_back_to_legacy_dev_mode_flag()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Reactions:DevMode"] = "true"
        });

        ZkVerifierModeResolver.Resolve(config).Should().Be(ZkVerifierMode.Dev);
    }

    [Fact]
    public void Resolve_throws_for_unknown_mode()
    {
        var config = BuildConfig(new Dictionary<string, string?>
        {
            ["Reactions:VerifierMode"] = "broken"
        });

        var action = () => ZkVerifierModeResolver.Resolve(config);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*Reactions:VerifierMode*");
    }

    private static IConfiguration BuildConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values)
            .Build();
}
