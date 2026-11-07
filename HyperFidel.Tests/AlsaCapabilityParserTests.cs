using HyperFidel.Audio;
using Xunit;

namespace HyperFidel.Tests;

/// <summary>Checks ALSA stream capability parsing used by the Linux device info dialog.</summary>
public sealed class AlsaCapabilityParserTests
{
    /// <summary>Repeated altset lines collapse to one ascending, distinct rate list.</summary>
    [Fact]
    public void ParseRateLines_CollectsDistinctRatesAboveEightKilohertz()
    {
        var lines = new[]
        {
            "Playback:",
            "    Altset 1",
            "    Rates: 8000, 16000, 32000, 44100, 48000, 96000, 192000, 384000",
            "    Altset 2",
            "    Rates: 8000, 44100, 192000, 384000",
            "    Format: S32_LE"
        };

        Assert.Equal(
            [8000, 16000, 32000, 44100, 48000, 96000, 192000, 384000],
            AlsaCapabilityParser.ParseRateLines(lines));
    }

    /// <summary>Files without a rate line produce no rates instead of guessing.</summary>
    [Fact]
    public void ParseRateLines_IgnoresLinesWithoutRates() =>
        Assert.Empty(AlsaCapabilityParser.ParseRateLines(["Playback:", "    Format: S32_LE"]));
}
