using System.Text.Json;

namespace HyperFidel.Library;

/// <summary>
/// Serializes and combines supplemental MusicBrainz genre metadata that is stored
/// as local, dormant library data. This type performs no network access; it only
/// composes genre text already present in the local database for search and
/// classification.
/// </summary>
public static class MusicBrainzGenreMetadata
{
    /// <summary>Serializes bounded normalized names for database storage.</summary>
    /// <param name="values">Genre or tag names.</param>
    /// <returns>A JSON array, or <see langword="null"/> when no names remain.</returns>
    public static string? Serialize(IEnumerable<string>? values)
    {
        var normalized = Normalize(values).ToList();
        return normalized.Count == 0 ? null : JsonSerializer.Serialize(normalized);
    }

    /// <summary>Combines embedded genres with separately stored MusicBrainz genres and tags.</summary>
    /// <param name="embeddedGenre">Unmodified embedded genre text.</param>
    /// <param name="genresJson">Stored MusicBrainz genre JSON.</param>
    /// <param name="tagsJson">Stored MusicBrainz tag JSON.</param>
    /// <returns>Comma-separated effective genre text for search and classification.</returns>
    public static string? Combine(string? embeddedGenre, string? genresJson, string? tagsJson)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(embeddedGenre))
            values.Add(embeddedGenre.Trim());
        values.AddRange(Deserialize(genresJson));
        values.AddRange(Deserialize(tagsJson));
        var normalized = Normalize(values).ToList();
        return normalized.Count == 0 ? null : string.Join(", ", normalized);
    }

    private static IEnumerable<string> Deserialize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return [];
        try { return JsonSerializer.Deserialize<List<string>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IEnumerable<string> Normalize(IEnumerable<string>? values) =>
        (values ?? [])
            .Select(value => value?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(32);
}
