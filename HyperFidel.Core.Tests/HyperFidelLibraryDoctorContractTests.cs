using System.Text.Json;
using HyperFidel.Library;
using HyperFidel.Streaming;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>Verifies the compact remote Library Doctor wire contract.</summary>
public sealed class HyperFidelLibraryDoctorContractTests
{
    /// <summary>Round-trips typed findings without embedding tracks or credentials.</summary>
    [Fact]
    public void Candidate_RoundTripsAsCompactJson()
    {
        var candidate = new HyperFidelLibraryDoctorCandidate(
            "/music/album",
            12,
            LibraryDoctorSeverity.Warning,
            [new HyperFidelLibraryDoctorFinding(
                "replaygain",
                LibraryDoctorSeverity.Warning,
                3,
                LibraryDoctorRepairCapability.MaintenanceAction)]);

        var json = JsonSerializer.Serialize(candidate);
        var restored = JsonSerializer.Deserialize<HyperFidelLibraryDoctorCandidate>(json);

        Assert.NotNull(restored);
        Assert.Equal(12, restored.TrackCount);
        Assert.Equal("replaygain", Assert.Single(restored.Findings).Code);
        Assert.DoesNotContain("ApiKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Tracks", json, StringComparison.OrdinalIgnoreCase);
    }
}
