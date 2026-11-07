namespace HyperFidel.Audio;

/// <summary>Parses ALSA <c>/proc/asound</c> capability files without opening the device.</summary>
internal static class AlsaCapabilityParser
{
    /// <summary>
    /// Extracts the distinct sample rates from ALSA stream capability lines. Each playback
    /// altset repeats a line such as <c>Rates: 44100, 48000, 192000, 384000</c>.
    /// </summary>
    /// <param name="lines">Lines of a <c>/proc/asound/cardN/streamM</c> file.</param>
    /// <returns>Ascending distinct rates of 8 kHz and above.</returns>
    internal static IReadOnlyList<int> ParseRateLines(IEnumerable<string> lines)
    {
        var rates = new SortedSet<int>();
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Rates:", StringComparison.Ordinal))
                continue;
            foreach (var part in trimmed["Rates:".Length..].Split(','))
                if (int.TryParse(part.Trim(), out var rate) && rate >= 8_000)
                    rates.Add(rate);
        }
        return [.. rates];
    }
}
