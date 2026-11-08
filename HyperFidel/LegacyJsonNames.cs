using System.Text.Json.Nodes;

namespace HyperFidel;

/// <summary>
/// Renames legacy Orynivo- and HyperFi-prefixed JSON properties to their HyperFidel
/// equivalents so settings and credential files from an earlier installation keep
/// loading instead of silently falling back to defaults.
/// </summary>
internal static class LegacyJsonNames
{
    private const string LegacyPrefix = "Orynivo";

    /// <summary>Prefix used before the product was renamed to HyperFidel.</summary>
    private const string HyperFiPrefix = "HyperFi";

    /// <summary>The current product prefix, which shares a stem with the legacy one.</summary>
    private const string CurrentPrefix = "HyperFidel";

    /// <summary>Rewrites every JSON object key that uses a historical product prefix.</summary>
    /// <param name="json">Serialized JSON document.</param>
    /// <returns>JSON with renamed keys, or the original text when it cannot be parsed.</returns>
    public static string RenameLegacyProperties(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        JsonNode? root;
        try
        {
            root = JsonNode.Parse(json);
        }
        catch
        {
            return json;
        }

        if (root is null)
            return json;

        RenameNode(root);
        return root.ToJsonString();
    }

    /// <summary>
    /// Maps a historical key onto its current name, or returns <see langword="null"/>
    /// when the key does not use a historical prefix. Keys that are already current
    /// are left alone, so <c>HyperFidelServers</c> is never rewritten to
    /// <c>HyperFidelFidelServers</c>.
    /// </summary>
    /// <param name="key">JSON property name to inspect.</param>
    /// <returns>The current property name, or <see langword="null"/> without a match.</returns>
    private static string? CurrentNameFor(string key)
    {
        if (key.StartsWith(LegacyPrefix, StringComparison.Ordinal))
            return CurrentPrefix + key[LegacyPrefix.Length..];

        if (key.StartsWith(CurrentPrefix, StringComparison.Ordinal))
            return null;

        if (key.StartsWith(HyperFiPrefix, StringComparison.Ordinal))
            return CurrentPrefix + key[HyperFiPrefix.Length..];

        return null;
    }

    /// <summary>Recursively renames object keys and visits every child value.</summary>
    /// <param name="node">Node to inspect.</param>
    private static void RenameNode(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var keys = obj.Select(pair => pair.Key).ToArray();
                foreach (var key in keys)
                {
                    var renamed = CurrentNameFor(key);
                    if (renamed is null)
                        continue;

                    if (!obj.ContainsKey(renamed))
                        obj[renamed] = obj[key]?.DeepClone();
                    obj.Remove(key);
                }

                foreach (var pair in obj)
                    RenameNode(pair.Value);
                break;
            case JsonArray array:
                foreach (var item in array)
                    RenameNode(item);
                break;
        }
    }
}
