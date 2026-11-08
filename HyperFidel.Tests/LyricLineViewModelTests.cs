using System;
using HyperFidel.Library;
using Xunit;

namespace HyperFidel.Tests;

/// <summary>
/// Verifies the word-progress state that drives the dynamic lyrics highlight.
/// </summary>
public sealed class LyricLineViewModelTests
{
    /// <summary>Words light up as their timestamps pass and reset when playback seeks back.</summary>
    [Fact]
    public void UpdateWordProgress_TracksPlaybackPositionBothWays()
    {
        var line = new LyricLineViewModel(
            "one two three",
            TimeSpan.Zero,
            [
                new TimedLyricWord(TimeSpan.Zero, "one"),
                new TimedLyricWord(TimeSpan.FromSeconds(1), "two"),
                new TimedLyricWord(TimeSpan.FromSeconds(2), "three")
            ]);

        line.UpdateWordProgress(TimeSpan.FromSeconds(1.5));

        Assert.True(line.HasWords);
        Assert.Equal([true, true, false], Sung(line));

        line.UpdateWordProgress(TimeSpan.FromSeconds(0.5));

        Assert.Equal([true, false, false], Sung(line));
    }

    /// <summary>A plain synchronized line exposes no word items.</summary>
    [Fact]
    public void PlainLine_HasNoWordItems()
    {
        var line = new LyricLineViewModel("plain", TimeSpan.Zero);

        Assert.False(line.HasWords);
        Assert.Empty(line.WordItems);
    }

    /// <summary>Collects the word highlight flags in order.</summary>
    /// <param name="line">Line to inspect.</param>
    /// <returns>One flag per word.</returns>
    private static bool[] Sung(LyricLineViewModel line)
    {
        var result = new bool[line.WordItems.Count];
        for (var index = 0; index < result.Length; index++)
            result[index] = line.WordItems[index].IsSung;
        return result;
    }
}
