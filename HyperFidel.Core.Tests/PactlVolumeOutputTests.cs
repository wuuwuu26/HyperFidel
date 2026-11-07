using HyperFidel.Audio;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>
/// Verifies that the C-locale <c>pactl</c> output used for direct-ALSA volume
/// mirroring is parsed without depending on the desktop language.
/// </summary>
public sealed class PactlVolumeOutputTests
{
    /// <summary>The first percentage of a normal sink-volume line is returned.</summary>
    [Fact]
    public void ParsePercent_NormalOutput_ReturnsFirstPercentage()
        => Assert.Equal(
            42,
            PactlVolumeOutput.ParsePercent(
                "Volume: front-left: 27525 /  42% / -22.61 dB,   " +
                "front-right: 27525 /  42% / -22.61 dB"));

    /// <summary>A full-scale volume is accepted.</summary>
    [Fact]
    public void ParsePercent_FullScale_ReturnsHundred()
        => Assert.Equal(100, PactlVolumeOutput.ParsePercent("Volume: 100%"));

    /// <summary>Text without a percentage is treated as unavailable.</summary>
    [Fact]
    public void ParsePercent_MissingPercentage_ReturnsNull()
        => Assert.Null(PactlVolumeOutput.ParsePercent("Volume: front-left: 27525"));

    /// <summary>A percentage above full scale is rejected instead of being clamped.</summary>
    [Fact]
    public void ParsePercent_OutOfRange_ReturnsNull()
        => Assert.Null(PactlVolumeOutput.ParsePercent("Volume: 250%"));

    /// <summary>An explicit positive mute report is detected.</summary>
    [Fact]
    public void ParseMuted_Yes_ReturnsTrue()
        => Assert.True(PactlVolumeOutput.ParseMuted("Mute: yes"));

    /// <summary>An explicit negative mute report is detected.</summary>
    [Fact]
    public void ParseMuted_No_ReturnsFalse()
        => Assert.False(PactlVolumeOutput.ParseMuted("Mute: no"));

    /// <summary>Missing output is treated as unmuted so audio is not silenced by accident.</summary>
    [Fact]
    public void ParseMuted_MissingOutput_ReturnsFalse()
        => Assert.False(PactlVolumeOutput.ParseMuted(null));

    /// <summary>Sink changes trigger a volume refresh.</summary>
    [Theory]
    [InlineData("Event 'change' on sink #53")]
    [InlineData("Event 'new' on sink #77")]
    [InlineData("Event 'remove' on sink #12")]
    public void IsSinkEvent_SinkLines_ReturnTrue(string line)
        => Assert.True(PactlVolumeOutput.IsSinkEvent(line));

    /// <summary>Unrelated subscription events do not trigger a refresh.</summary>
    [Theory]
    [InlineData("Event 'change' on card #48")]
    [InlineData("Event 'new' on client #2488")]
    [InlineData("")]
    public void IsSinkEvent_OtherLines_ReturnFalse(string line)
        => Assert.False(PactlVolumeOutput.IsSinkEvent(line));
}
