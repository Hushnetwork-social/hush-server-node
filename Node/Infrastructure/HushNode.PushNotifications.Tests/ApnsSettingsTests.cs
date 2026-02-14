using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace HushNode.PushNotifications.Tests;

/// <summary>
/// Tests for ApnsSettings configuration binding.
/// Each test follows AAA pattern with isolated setup.
/// </summary>
public class ApnsSettingsTests
{
    #region Full Binding Tests

    [Fact]
    public void ApnsSettings_WithAllFieldsPopulated_BindsCorrectly()
    {
        // Arrange
        var configuration = CreateConfiguration(
            enabled: true,
            keyId: "ABC1234567",
            teamId: "TEAM123456",
            bundleId: "social.hushnetwork.feeds",
            privateKeyPath: "/path/to/key.p8",
            useSandbox: false);

        // Act
        var settings = BindSettings(configuration);

        // Assert
        settings.Enabled.Should().BeTrue();
        settings.KeyId.Should().Be("ABC1234567");
        settings.TeamId.Should().Be("TEAM123456");
        settings.BundleId.Should().Be("social.hushnetwork.feeds");
        settings.PrivateKeyPath.Should().Be("/path/to/key.p8");
        settings.UseSandbox.Should().BeFalse();
    }

    #endregion

    #region Default Value Tests

    [Fact]
    public void ApnsSettings_WithEmptySection_HasCorrectDefaults()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        // Act
        var settings = BindSettings(configuration);

        // Assert
        settings.Enabled.Should().BeFalse();
        settings.UseSandbox.Should().BeTrue();
        settings.KeyId.Should().BeNull();
        settings.TeamId.Should().BeNull();
        settings.BundleId.Should().BeNull();
        settings.PrivateKeyPath.Should().BeNull();
    }

    #endregion

    #region Partial Configuration Tests

    [Fact]
    public void ApnsSettings_WithOnlyEnabledTrue_HasNullStringFields()
    {
        // Arrange
        var configuration = CreateConfiguration(
            enabled: true,
            keyId: null,
            teamId: null,
            bundleId: null,
            privateKeyPath: null,
            useSandbox: true);

        // Act
        var settings = BindSettings(configuration);

        // Assert
        settings.Enabled.Should().BeTrue();
        settings.KeyId.Should().BeNull();
        settings.TeamId.Should().BeNull();
        settings.BundleId.Should().BeNull();
        settings.PrivateKeyPath.Should().BeNull();
        settings.UseSandbox.Should().BeTrue();
    }

    #endregion

    #region IOptions Binding Tests

    [Fact]
    public void ApnsSettings_ViaIOptions_BindsCorrectly()
    {
        // Arrange
        var configuration = CreateConfiguration(
            enabled: true,
            keyId: "KEY1234567",
            teamId: "TEAM999999",
            bundleId: "com.example.app",
            privateKeyPath: "./secrets/apns-key.p8",
            useSandbox: true);

        var services = new ServiceCollection();
        services.Configure<ApnsSettings>(configuration.GetSection("ApnsSettings"));
        var serviceProvider = services.BuildServiceProvider();

        // Act
        var options = serviceProvider.GetRequiredService<IOptions<ApnsSettings>>();
        var settings = options.Value;

        // Assert
        settings.Enabled.Should().BeTrue();
        settings.KeyId.Should().Be("KEY1234567");
        settings.TeamId.Should().Be("TEAM999999");
        settings.BundleId.Should().Be("com.example.app");
        settings.PrivateKeyPath.Should().Be("./secrets/apns-key.p8");
        settings.UseSandbox.Should().BeTrue();
    }

    #endregion

    #region Helper Methods

    private static IConfiguration CreateConfiguration(
        bool enabled,
        string? keyId,
        string? teamId,
        string? bundleId,
        string? privateKeyPath,
        bool useSandbox)
    {
        var configData = new Dictionary<string, string?>
        {
            ["ApnsSettings:Enabled"] = enabled.ToString(),
            ["ApnsSettings:KeyId"] = keyId,
            ["ApnsSettings:TeamId"] = teamId,
            ["ApnsSettings:BundleId"] = bundleId,
            ["ApnsSettings:PrivateKeyPath"] = privateKeyPath,
            ["ApnsSettings:UseSandbox"] = useSandbox.ToString()
        };

        return new ConfigurationBuilder()
            .AddInMemoryCollection(configData)
            .Build();
    }

    private static ApnsSettings BindSettings(IConfiguration configuration)
    {
        var settings = new ApnsSettings();
        configuration.GetSection("ApnsSettings").Bind(settings);
        return settings;
    }

    #endregion
}
