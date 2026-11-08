using System.Text.Json.Nodes;

namespace HyperFidel;

/// <summary>
/// Renames legacy Orynivo-prefixed JSON properties to their HyperFidel equivalents so
/// settings and credential files copied from an earlier installation keep loading.
/// </summary>
internal static class LegacyJsonNames
{
    private const string LegacyPrefix = "Orynivo";
    /// <summary>Rewrites every JSON object key that starts with <c>Orynivo</c>.</summary>
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
                    if (key.StartsWith(LegacyPrefix, StringComparison.Ordinal))
                    {
                        var renamed = "HyperFidel" + key[LegacyPrefix.Length..];
                        if (!obj.ContainsKey(renamed))
                            obj[renamed] = obj[key]?.DeepClone();
                        obj.Remove(key);
                    }
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
