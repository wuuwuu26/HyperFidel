using Microsoft.Extensions.Configuration;
using Xunit;

namespace HyperFidel.Server.Tests;

/// <summary>Checks that upgraded servers keep reading their previous configuration root.</summary>
public sealed class ServerSettingsSectionTests
{
    /// <summary>Builds configuration from an in-memory dictionary.</summary>
    /// <param name="values">Configuration keys and values.</param>
    /// <returns>The configuration root.</returns>
    private static IConfiguration Build(params (string Key, string Value)[] values)
        => new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    /// <summary>The current root wins when it is present.</summary>
    [Fact]
    public void Resolve_PrefersCurrentSection()
    {
        var configuration = Build(
            ("HyperFidel:ApiKey", "new-key"),
            ("HyperFi:ApiKey", "old-key"),
            ("Orynivo:ApiKey", "older-key"));

        var settings = ServerSettingsSection.Resolve(configuration).Get<ServerSettings>();

        Assert.NotNull(settings);
        Assert.Equal("new-key", settings!.ApiKey);
    }

    /// <summary>A server upgraded from HyperFi keeps its configured values.</summary>
    [Fact]
    public void Resolve_FallsBackToHyperFiSection()
    {
        var configuration = Build(("HyperFi:ApiKey", "old-key"));

        var settings = ServerSettingsSection.Resolve(configuration).Get<ServerSettings>();

        Assert.NotNull(settings);
        Assert.Equal("old-key", settings!.ApiKey);
    }

    /// <summary>A server upgraded from Orynivo keeps its configured values.</summary>
    [Fact]
    public void Resolve_FallsBackToOrynivoSection()
    {
        var configuration = Build(("Orynivo:ApiKey", "older-key"));

        var settings = ServerSettingsSection.Resolve(configuration).Get<ServerSettings>();

        Assert.NotNull(settings);
        Assert.Equal("older-key", settings!.ApiKey);
    }

    /// <summary>Without any section the current name is returned for defaulting.</summary>
    [Fact]
    public void Resolve_WithoutAnySectionReturnsCurrentName()
    {
        var section = ServerSettingsSection.Resolve(Build());

        Assert.Equal("HyperFidel", section.Key);
        Assert.False(section.Exists());
    }
}
