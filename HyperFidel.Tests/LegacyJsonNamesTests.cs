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
}
