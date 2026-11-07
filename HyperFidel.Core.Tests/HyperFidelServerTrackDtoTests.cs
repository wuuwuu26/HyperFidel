using System.Text.Json;
using HyperFidel.Streaming;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>
/// Guards the remote track DTO fields the desktop relies on. A cached Camelot key
/// once landed on the rating-mutation DTO instead of the track DTO, so remote
/// rows silently lost it.
/// </summary>
public sealed class HyperFidelServerTrackDtoTests
{
    /// <summary>The track DTO carries the cached Camelot wheel label.</summary>
    [Fact]
    public void HyperFidelTrackInfo_DeserializesCamelotKey()
    {
        const string json = """
            {
              "Id": 7,
              "Path": "/music/album/track.flac",
              "FileName": "track.flac",
              "Title": "Track",
              "CamelotKey": "8A"
            }
            """;

        var track = JsonSerializer.Deserialize<HyperFidelTrackInfo>(json);

        Assert.NotNull(track);
        Assert.Equal("8A", track.CamelotKey);
    }

    /// <summary>A track DTO without a key stays valid for older servers.</summary>
    [Fact]
    public void HyperFidelTrackInfo_WithoutCamelotKeyRemainsValid()
    {
        const string json = """{"Id":7,"Path":"/music/album/track.flac","FileName":"track.flac"}""";

        var track = JsonSerializer.Deserialize<HyperFidelTrackInfo>(json);

        Assert.NotNull(track);
        Assert.Null(track.CamelotKey);
    }
}
