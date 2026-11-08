using Microsoft.Extensions.Configuration;

namespace HyperFidel.Server;

/// <summary>
/// Resolves the configuration section that holds <see cref="ServerSettings"/>.
/// </summary>
/// <remarks>
/// Earlier releases stored the settings under the <c>HyperFi</c> and <c>Orynivo</c>
/// roots. Reading the current root first and falling back through the historical
/// ones lets an upgraded server keep its ApiKey, library paths and profiles
/// instead of silently reverting to the shipped defaults.
/// </remarks>
public static class ServerSettingsSection
{
    /// <summary>Section names in precedence order, newest first.</summary>
    private static readonly string[] SectionNames = ["HyperFidel", "HyperFi", "Orynivo"];

    /// <summary>
    /// Returns the first existing settings section, or the current section when
    /// none exists so callers still receive a usable (defaulted) section.
    /// </summary>
    /// <param name="configuration">Application configuration to read.</param>
    /// <returns>The resolved settings section.</returns>
    public static IConfigurationSection Resolve(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (var name in SectionNames)
        {
            var section = configuration.GetSection(name);
            if (section.Exists())
                return section;
        }

        return configuration.GetSection(SectionNames[0]);
    }
}
