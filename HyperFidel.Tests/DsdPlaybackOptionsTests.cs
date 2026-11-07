using HyperFidel.Audio;
using Xunit;

namespace HyperFidel.Tests;

/// <summary>Checks the DSD to PCM rate resolution shared by every output path.</summary>
public sealed class DsdPlaybackOptionsTests
{
    /// <summary>An explicit rate is used as-is; zero and out-of-range values fall back to the conventional rate.</summary>
    /// <param name="configured">Value stored in the setting.</param>
    /// <param name="expected">Rate the players must use.</param>
    [Theory]
    [InlineData(384_000, 384_000)]
    [InlineData(352_800, 352_800)]
    [InlineData(192_000, 192_000)]
    [InlineData(0, DsdPlaybackOptions.ConventionalPcmRate)]
    [InlineData(1_000, DsdPlaybackOptions.ConventionalPcmRate)]
    [InlineData(1_000_000, DsdPlaybackOptions.ConventionalPcmRate)]
    public void ResolveOutputRate_UsesConfiguredOrConventionalRate(int configured, int expected)
    {
        var original = DsdPlaybackOptions.PcmSampleRateHz;
        try
        {
            DsdPlaybackOptions.PcmSampleRateHz = configured;
            Assert.Equal(expected, DsdPlaybackOptions.ResolveOutputRate());
        }
        finally
        {
            DsdPlaybackOptions.PcmSampleRateHz = original;
        }
    }

    /// <summary>A pre-known DSD stream takes the configured conversion rate, not its source rate.</summary>
    [Fact]
    public void KnownDsdInfo_UsesConfiguredPcmRate()
    {
        var original = DsdPlaybackOptions.PcmSampleRateHz;
        try
        {
            DsdPlaybackOptions.PcmSampleRateHz = 192_000;
            var item = new GaplessPlaybackItem(
                "synthetic.dsf",
                1.0f,
                KnownInfo: new KnownAudioInfo(2_822_400, 2, "dsd_lsbf_planar", true, "dsf"));

            var info = item.TryCreateKnownAudioInfo();

            Assert.NotNull(info);
            Assert.True(info.IsDsd);
            Assert.Equal(192_000, info.OutputSampleRate);
            Assert.Equal(2_822_400, info.SourceSampleRate);
        }
        finally
        {
            DsdPlaybackOptions.PcmSampleRateHz = original;
        }
    }

    /// <summary>PCM sources keep their own rate regardless of the DSD conversion setting.</summary>
    [Fact]
    public void KnownPcmInfo_KeepsSourceRate()
    {
        var original = DsdPlaybackOptions.PcmSampleRateHz;
        try
        {
            DsdPlaybackOptions.PcmSampleRateHz = 192_000;
            var item = new GaplessPlaybackItem(
                "synthetic.flac",
                1.0f,
                KnownInfo: new KnownAudioInfo(44_100, 2, "flac", false, "flac"));

            var info = item.TryCreateKnownAudioInfo();

            Assert.NotNull(info);
            Assert.Equal(44_100, info.OutputSampleRate);
        }
        finally
        {
            DsdPlaybackOptions.PcmSampleRateHz = original;
        }
    }
}
