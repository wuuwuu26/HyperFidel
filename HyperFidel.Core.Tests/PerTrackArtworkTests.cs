using HyperFidel.Library;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>
/// Verifies that an individual track's embedded cover is preferred over the
/// shared album cover, so tracks in one album can show different images.
/// </summary>
public sealed class PerTrackArtworkTests
{
    /// <summary>
    /// Two tracks in one album with different embedded covers return their own
    /// cover files rather than the single album-level cover.
    /// </summary>
    [Fact]
    public void GetArtworkPathsByTrackPath_PrefersTrackCoverOverAlbumCover()
    {
        var root = Path.Combine(
            Path.GetTempPath(), $"hyperfidel-pertrack-art-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var trackA = Path.Combine(root, "a.flac");
        var trackB = Path.Combine(root, "b.flac");
        var coverA = new byte[] { 1, 2, 3, 4, 5 };
        var coverB = new byte[] { 9, 8, 7, 6, 5 };
        try
        {
            using (var db = new AudioDatabase(databasePath))
            {
                db.Upsert(CreateTrack(trackA, "Shared album", coverA));
                db.Upsert(CreateTrack(trackB, "Shared album", coverB));
            }

            using var reopened = new AudioDatabase(databasePath);
            var pathsA = reopened.GetArtworkPathsByTrackPath(trackA);
            var pathsB = reopened.GetArtworkPathsByTrackPath(trackB);

            Assert.NotNull(pathsA);
            Assert.NotNull(pathsB);
            Assert.NotNull(pathsA.OriginalPath);
            Assert.NotNull(pathsB.OriginalPath);
            Assert.NotEqual(
                pathsA.OriginalPath, pathsB.OriginalPath);
            Assert.Equal(coverA, File.ReadAllBytes(pathsA.OriginalPath));
            Assert.Equal(coverB, File.ReadAllBytes(pathsB.OriginalPath));
        }
        finally
        {
            CoreTestDatabase.ClearPool(databasePath);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>
    /// A track without its own embedded cover still falls back to the album
    /// cover.
    /// </summary>
    [Fact]
    public void GetArtworkPathsByTrackPath_FallsBackToAlbumCover()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            $"hyperfidel-pertrack-fallback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var withCover = Path.Combine(root, "with.flac");
        var withoutCover = Path.Combine(root, "without.flac");
        var cover = new byte[] { 4, 2, 0 };
        try
        {
            using (var db = new AudioDatabase(databasePath))
            {
                db.Upsert(CreateTrack(withCover, "Album", cover));
                db.Upsert(CreateTrack(withoutCover, "Album", null));
            }

            using var reopened = new AudioDatabase(databasePath);
            var withPaths = reopened.GetArtworkPathsByTrackPath(withCover);
            var withoutPaths =
                reopened.GetArtworkPathsByTrackPath(withoutCover);

            Assert.NotNull(withPaths);
            Assert.NotNull(withoutPaths);
            Assert.Equal(withPaths.OriginalPath, withoutPaths.OriginalPath);
        }
        finally
        {
            CoreTestDatabase.ClearPool(databasePath);
            Directory.Delete(root, recursive: true);
        }
    }

    private static TrackRecord CreateTrack(
        string path, string album, byte[]? cover) => new()
        {
            Path = path,
            SourcePath = path,
            FileName = Path.GetFileName(path),
            ModifiedAt = 1,
            AddedAt = 1,
            Duration = 180,
            Title = Path.GetFileNameWithoutExtension(path),
            Artist = "Artist",
            AlbumArtist = "Artist",
            Album = album,
            TrackNumber = 1,
            CoverData = cover,
            CoverMimeType = cover is null ? null : "image/jpeg"
        };
}
