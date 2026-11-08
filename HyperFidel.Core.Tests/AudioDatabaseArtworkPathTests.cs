using Microsoft.Data.Sqlite;
using HyperFidel.Library;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>Verifies the dashboard artwork query returns full-resolution paths per album.</summary>
public sealed class AudioDatabaseArtworkPathTests
{
    /// <summary>Albums with a stored original artwork are returned; albums without one are omitted.</summary>
    [Fact]
    public void GetAlbumOriginalArtworkByTrackIds_ReturnsStoredOriginalPaths()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hyperfidel-artwork-paths-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        try
        {
            using var db = new AudioDatabase(databasePath);
            db.Upsert(CreateTrack(Path.Combine(root, "with", "a.flac"), "With art"));
            db.Upsert(CreateTrack(Path.Combine(root, "without", "b.flac"), "Without art"));

            var withArt = db.GetAlbumsLite().Single(row => row.Album == "With art");
            var withoutArt = db.GetAlbumsLite().Single(row => row.Album == "Without art");
            var withTrackId = db.GetTrackListByAlbum(withArt.Id).Single().Id;
            var withoutTrackId = db.GetTrackListByAlbum(withoutArt.Id).Single().Id;

            using (var connection = new SqliteConnection($"Data Source={databasePath}"))
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO artworks (content_hash, mime_type, data, original_path, thumb_96_path, thumb_320_path)
                    VALUES ('SYNTHETIC', 'image/jpeg', x'00', '/tmp/original.jpg', '/tmp/96.jpg', '/tmp/320.jpg');
                    UPDATE albums SET artwork_id = last_insert_rowid() WHERE id = $album;
                    """;
                command.Parameters.AddWithValue("$album", withArt.Id);
                command.ExecuteNonQuery();
            }

            var paths = db.GetAlbumOriginalArtworkByTrackIds([withTrackId, withoutTrackId]);

            Assert.Equal("/tmp/original.jpg", paths[withArt.Id]);
            Assert.False(paths.ContainsKey(withoutArt.Id));
            Assert.Empty(db.GetAlbumOriginalArtworkByTrackIds([]));
        }
        finally
        {
            CoreTestDatabase.ClearPool(databasePath);
            Directory.Delete(root, recursive: true);
        }
    }

    private static TrackRecord CreateTrack(string path, string album) =>
        new()
        {
            Path = path,
            SourcePath = path,
            FileName = Path.GetFileName(path),
            ModifiedAt = 1,
            AddedAt = 1,
            Duration = 180,
            Title = "Track",
            Artist = "Artist",
            AlbumArtist = "Artist",
            Album = album,
            TrackNumber = 1
        };
}
