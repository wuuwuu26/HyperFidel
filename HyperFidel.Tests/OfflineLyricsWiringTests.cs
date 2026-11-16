using Xunit;

namespace HyperFidel.Tests;

/// <summary>
/// Guards that the desktop lyrics page is offline: it reads local sidecar
/// <c>.lrc</c> files and embedded tags only, and never performs an online
/// lyrics download. The decoding itself is covered by LocalLyricsLoaderTests.
/// </summary>
public sealed class OfflineLyricsWiringTests
{
    private static string FindRepoFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(relativePath);
    }

    /// <summary>The local provider must fall back to a sidecar .lrc file.</summary>
    [Fact]
    public void LocalProvider_UsesSidecarLyricsLoader()
    {
        var source = FindRepoFile(Path.Combine("HyperFidel", "NowPlayingMetadataProviders.cs"));

        Assert.Contains("TryLoadLocalLyrics", source);
    }

    /// <summary>The lyrics view must not start an online download.</summary>
    [Fact]
    public void LyricsView_DoesNotDownloadOnline()
    {
        var source = FindRepoFile(
            Path.Combine("HyperFidel", "MainWindow.Transport.cs"));

        Assert.DoesNotContain("DownloadLyricsAsync", source);
        Assert.DoesNotContain("LyricsDownloading", source);
    }
}
