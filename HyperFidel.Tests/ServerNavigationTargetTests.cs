using Xunit;

namespace HyperFidel.Tests;

/// <summary>Protects credential-free server routing and album source ownership without a window.</summary>
public sealed class ServerNavigationTargetTests
{
    /// <summary>Non-server and malformed tags cannot resolve against ambient server state.</summary>
    /// <param name="tag">Invalid navigation identity.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Tracks")]
    [InlineData("PlexLibrary:fixture:1")]
    [InlineData("HyperFidelServerPlaylist:fixture:1")]
    [InlineData("hyperfidelserver:fixture:Albums")]
    [InlineData("HyperFidelServer:")]
    [InlineData("HyperFidelServer: :Albums")]
    [InlineData("HyperFidelServer::Albums")]
    public void InvalidTagsHaveNoTarget(string? tag)
    {
        Assert.False(ServerNavigationTarget.TryParse(tag, out var target));
        Assert.Null(target);
    }

    /// <summary>The parser preserves legacy Root defaults and existing view grammar.</summary>
    /// <param name="tag">Existing server tag.</param>
    /// <param name="view">Expected parsed view.</param>
    [Theory]
    [InlineData("HyperFidelServer:fixture", "Root")]
    [InlineData("HyperFidelServer:fixture:", "Root")]
    [InlineData("HyperFidelServer:fixture: ", "Root")]
    [InlineData("HyperFidelServer:fixture:Artists", "Artists")]
    [InlineData("HyperFidelServer:fixture:Albums", "Albums")]
    [InlineData("HyperFidelServer:fixture:Tracks", "Tracks")]
    [InlineData("HyperFidelServer:fixture:Folders", "Folders")]
    [InlineData("HyperFidelServer:fixture:Legacy:Detail", "Legacy:Detail")]
    public void ExistingTagsRetainTheirServerAndView(string tag, string view)
    {
        Assert.True(ServerNavigationTarget.TryParse(tag, out var target));
        Assert.Equal("fixture", target!.ServerId);
        Assert.Equal(view, target.View);
        Assert.Equal($"HyperFidelServer:fixture:{view}", target.Tag);
    }

    /// <summary>Album capture cannot inherit a foreign server's tag, even for matching numeric IDs.</summary>
    /// <param name="origin">Sidebar origin unrelated to the owning source.</param>
    [Theory]
    [InlineData(null)]
    [InlineData("Dashboard")]
    [InlineData("HyperFidelServer:other:Albums")]
    [InlineData("HyperFidelServer:FIXTURE:Albums")]
    [InlineData("HyperFidelServer::Albums")]
    public void AlbumTargetRepairsForeignOrInvalidOrigin(string? origin)
    {
        Assert.Equal("HyperFidelServer:fixture:Albums",
            ServerNavigationTarget.ForAlbum("fixture", origin));
        var capture = NavigationCaptureDecision.Select(new(false, false, origin, 42, null,
            null, false, true, "fixture", false));
        Assert.Equal(NavigationCaptureKind.ServerAlbum, capture.Kind);
        Assert.Equal("HyperFidelServer:fixture:Albums", capture.NavigationTag);
    }

    /// <summary>An origin from the owning source keeps its existing view.</summary>
    [Fact]
    public void AlbumTargetRetainsMatchingOrigin()
    {
        Assert.Equal("HyperFidelServer:fixture:Tracks",
            ServerNavigationTarget.ForAlbum("fixture", "HyperFidelServer:fixture:Tracks"));
        Assert.Equal("HyperFidelServer:fixture", ServerNavigationTarget.ForAlbum("fixture", "HyperFidelServer:fixture"));
        Assert.Equal("HyperFidelServer:fixture:", ServerNavigationTarget.ForAlbum("fixture", "HyperFidelServer:fixture:"));
    }
}
