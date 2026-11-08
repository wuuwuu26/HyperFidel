using Xunit;

namespace HyperFidel.Tests;

/// <summary>
/// Verifies migration of Orynivo-prefixed settings and credential JSON keys.
/// </summary>
public sealed class LegacyJsonNamesTests
{
    /// <summary>Top-level and nested Orynivo keys are renamed without losing values.</summary>
    [Fact]
    public void RenameLegacyProperties_RenamesTopLevelAndNestedKeys()
    {
        const string json = """
            {
              "OrynivoServers": [ { "Name": "server" } ],
              "OrynivoServerFavorites": [ "a" ],
              "UserProfiles": [ { "OrynivoServerFavorites": [ "b" ] } ],
              "Unrelated": 1
            }
            """;

        var renamed = LegacyJsonNames.RenameLegacyProperties(json);

        Assert.Contains("\"HyperFidelServers\"", renamed);
        Assert.Contains("\"HyperFidelServerFavorites\"", renamed);
        Assert.DoesNotContain("\"Orynivo", renamed);
        Assert.Contains("\"Unrelated\":1", renamed.Replace(" ", string.Empty));
    }

    /// <summary>An existing HyperFidel key wins over a legacy duplicate.</summary>
    [Fact]
    public void RenameLegacyProperties_PreservesCurrentKey()
    {
        const string json = """{"OrynivoServers":[],"HyperFidelServers":[{"Name":"current"}]}""";

        var renamed = LegacyJsonNames.RenameLegacyProperties(json);

        Assert.Contains("current", renamed);
        Assert.DoesNotContain("Orynivo", renamed);
    }

    /// <summary>Malformed JSON is returned unchanged.</summary>
    [Fact]
    public void RenameLegacyProperties_ReturnsInvalidJsonUnchanged()
    {
        const string json = "{not json";

        Assert.Equal(json, LegacyJsonNames.RenameLegacyProperties(json));
    }

    /// <summary>HyperFi-prefixed properties are renamed to their current names.</summary>
    [Fact]
    public void RenameLegacyProperties_RenamesHyperFiPrefix()
    {
        const string json = """{"HyperFiServers":[],"HyperFiServerFavorites":["a"]}""";

        var renamed = LegacyJsonNames.RenameLegacyProperties(json);

        Assert.Contains("\"HyperFidelServers\"", renamed);
        Assert.Contains("\"HyperFidelServerFavorites\"", renamed);
        Assert.DoesNotContain("\"HyperFiServers\"", renamed);
        Assert.DoesNotContain("\"HyperFiServerFavorites\"", renamed);
    }

    /// <summary>A current name is never rewritten onto itself.</summary>
    [Fact]
    public void RenameLegacyProperties_DoesNotDoublePrefixCurrentNames()
    {
        const string json = """{"HyperFidelServers":[],"HyperFidelServerFavorites":[]}""";

        var renamed = LegacyJsonNames.RenameLegacyProperties(json);

        Assert.DoesNotContain("HyperFidelFidel", renamed);
        Assert.Contains("\"HyperFidelServers\"", renamed);
    }

    /// <summary>A user-chosen volume is not product-prefixed and survives the rename.</summary>
    [Fact]
    public void RenameLegacyProperties_PreservesUnrelatedProperties()
    {
        const string json = """{"Volume":0.35,"HyperFiServers":[]}""";

        var renamed = LegacyJsonNames.RenameLegacyProperties(json);

        Assert.Contains("\"Volume\":0.35", renamed);
    }
}
