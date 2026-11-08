using System.Text.Json;
using Xunit;

namespace HyperFidel.Tests;

/// <summary>
/// Covers volume persistence rules: a saved volume must survive loading and an
/// unrelated settings problem must not silently reset it to full volume.
/// </summary>
public sealed class VolumePersistenceTests
{
    /// <summary>The persisted volume survives a full round trip.</summary>
    [Fact]
    public void Volume_RoundTripsThroughSettingsJson()
    {
        var json = JsonSerializer.Serialize(new AppSettings { Volume = 0.42 });
        var restored = JsonSerializer.Deserialize<AppSettings>(json);

        Assert.NotNull(restored);
        Assert.Equal(0.42, restored!.Volume, 6);
    }

    /// <summary>Settings written before the rename keep the volume the user chose.</summary>
    [Fact]
    public void LegacyPrefixedSettings_KeepVolume()
    {
        var normalized = LegacyJsonNames.RenameLegacyProperties(
            """{"Volume":0.35,"HyperFiServers":[]}""");
        var restored = JsonSerializer.Deserialize<AppSettings>(normalized);

        Assert.NotNull(restored);
        Assert.Equal(0.35, restored!.Volume, 6);
    }

    /// <summary>A volume of exactly one is preserved instead of being clamped away.</summary>
    [Fact]
    public void FullVolume_IsPreserved()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("""{"Volume":1.0}""");

        Assert.NotNull(restored);
        Assert.Equal(1.0, restored!.Volume, 6);
    }

    /// <summary>Silence is a valid saved value and must not become full volume.</summary>
    [Fact]
    public void SilentVolume_IsPreserved()
    {
        var restored = JsonSerializer.Deserialize<AppSettings>("""{"Volume":0.0}""");

        Assert.NotNull(restored);
        Assert.Equal(0.0, restored!.Volume, 6);
    }

    /// <summary>
    /// A credential payload with null collections is repairable, so it cannot abort
    /// loading and discard the saved volume.
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
