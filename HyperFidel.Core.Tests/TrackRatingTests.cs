using HyperFidel.Library;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>Verifies durable personal and MusicBrainz track ratings.</summary>
public sealed class TrackRatingTests
{
    /// <summary>Ensures scanner upserts preserve user-entered ratings and resolved recording identities.</summary>
    [Fact]
    public void Upsert_PreservesPersonalAndResolvedMusicBrainzRatings()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hyperfidel-rating-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var trackPath = Path.Combine(root, "track.flac");
        try
        {
            using (var database = new AudioDatabase(databasePath))
            {
                database.Upsert(CreateTrack(trackPath));
                var track = Assert.Single(database.GetTrackList());
                database.SetTrackUserRating(track.Id, 5);
                database.SetTrackMusicBrainzRating(
                    track.Id,
                    "c410a773-c6eb-4bc0-9df8-042fe6645c63",
                    4.2,
                    37,
                    1234567890,
                    MusicBrainzGenreMetadata.Serialize(["Synth-pop"]),
                    MusicBrainzGenreMetadata.Serialize(["80s"]));
                database.Upsert(CreateTrack(trackPath));
            }

            using var reopened = new AudioDatabase(databasePath);
            var persisted = Assert.Single(reopened.GetTrackList());
            Assert.Equal(5, persisted.UserRating);
            Assert.Equal(4.2, persisted.MusicBrainzRating);
            Assert.Equal(37, persisted.MusicBrainzRatingVotes);
            Assert.Equal("c410a773-c6eb-4bc0-9df8-042fe6645c63", persisted.MusicBrainzTrackId);
            Assert.Equal(1234567890, persisted.MusicBrainzRatingFetchedAt);
            Assert.Equal("Synth-pop", Assert.Single(System.Text.Json.JsonSerializer.Deserialize<List<string>>(persisted.MusicBrainzGenres!)!));
            Assert.Contains("Synth-pop", Assert.Single(reopened.GetTrackFacets()).Genre);
            Assert.Contains("80s", Assert.Single(reopened.GetTrackFacets()).Genre);
        }
        finally
        {
            CoreTestDatabase.ClearPool(databasePath);
            Directory.Delete(root, recursive: true);
        }
    }

    /// <summary>Ensures an unresolved lookup timestamp survives a later scanner upsert.</summary>
    [Fact]
    public void MusicBrainzLookupAttempt_PersistsWithoutResolvedMbid()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hyperfidel-rating-attempt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var databasePath = Path.Combine(root, "library.db");
        var trackPath = Path.Combine(root, "track.flac");
        try
        {
            using (var database = new AudioDatabase(databasePath))
            {
                database.Upsert(CreateTrack(trackPath));
                var track = Assert.Single(database.GetTrackList());
                database.SetTrackMusicBrainzLookupAttempt(track.Id, 1234567890);
                database.Upsert(CreateTrack(trackPath));
            }

            using var reopened = new AudioDatabase(databasePath);
            var persisted = Assert.Single(reopened.GetTrackList());
            Assert.Null(persisted.MusicBrainzTrackId);
            Assert.Equal(1234567890, persisted.MusicBrainzRatingFetchedAt);
        }
        finally
        {
            CoreTestDatabase.ClearPool(databasePath);
            Directory.Delete(root, recursive: true);
        }
    }

    private static TrackRecord CreateTrack(string path) => new()
    {
        Path = path,
        SourcePath = path,
        FileName = Path.GetFileName(path),
        ModifiedAt = 1,
        AddedAt = 1,
        Title = "Track",
        Artist = "Artist",
        AlbumArtist = "Artist",
        Album = "Album"
    };

}
