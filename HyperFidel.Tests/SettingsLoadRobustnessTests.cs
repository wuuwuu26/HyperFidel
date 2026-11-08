using System.Text.Json;
using Xunit;

namespace HyperFidel.Tests;

/// <summary>
/// Covers the settings-loading contract that keeps a saved volume from being
/// reset to full volume when an unrelated part of the file cannot be used.
/// </summary>
public sealed class SettingsLoadRobustnessTests
{
    /// <summary>
    /// Applies the same normalization pipeline the store uses before returning the
    /// parsed settings, so a failure here is what previously reset the volume.
    /// </summary>
    /// <param name="json">Persisted settings document.</param>
    /// <returns>The settings that survive loading.</returns>
    private static AppSettings LoadLikeStore(string json)
    {
        var normalized = LegacyJsonNames.RenameLegacyProperties(json);
        return JsonSerializer.Deserialize<AppSettings>(normalized) ?? new AppSettings();
    }

    /// <summary>HyperFi-prefixed files keep the volume the user chose.</summary>
    [Fact]
    public void LegacyPrefixedSettings_KeepVolume()
    {
        var settings = LoadLikeStore("""{"Volume":0.35,"HyperFiServers":[]}""");

        Assert.Equal(0.35, settings.Volume, 6);
        Assert.Empty(settings.HyperFidelServers);
    }

    /// <summary>A settings document written by the current release keeps its volume.</summary>
    [Fact]
    public void CurrentSettings_KeepVolume()
    {
        var settings = LoadLikeStore("""{"Volume":0.2,"HyperFidelServers":[]}""");

        Assert.Equal(0.2, settings.Volume, 6);
    }

    /// <summary>The persisted volume survives a full round trip.</summary>
    [Fact]
    public void Volume_RoundTripsThroughSettingsJson()
    {
        var json = JsonSerializer.Serialize(new AppSettings { Volume = 0.42 });
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(0.42, restored!.Volume, 6);
    }

    /// <summary>
    /// A credential snapshot with null collections is repairable instead of
    /// throwing, because a throw used to abort the whole load.
    /// </summary>
    [Fact]
    public void Snapshot_NullCollectionsAreRepairable()
    {
        var snapshot = JsonSerializer.Deserialize<ApplicationCredentialSnapshot>(
            """{"HyperFidelServerApiKeys":null,"PlexTokens":null}""");

        Assert.NotNull(snapshot);
        snapshot!.HyperFidelServerApiKeys ??= new(StringComparer.Ordinal);
        snapshot.PlexTokens ??= new(StringComparer.Ordinal);

        Assert.Empty(snapshot.HyperFidelServerApiKeys);
        Assert.Empty(snapshot.PlexTokens);
    }
}
