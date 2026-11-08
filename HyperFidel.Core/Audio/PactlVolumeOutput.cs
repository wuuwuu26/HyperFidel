using System.Globalization;

namespace HyperFidel.Audio;

/// <summary>
/// Parses the stable, C-locale output of <c>pactl</c> volume and subscription
/// queries used to mirror the Linux system output volume.
/// </summary>
public static class PactlVolumeOutput
{
    /// <summary>Extracts the first whole percentage from a sink-volume response.</summary>
    /// <param name="output">Complete or partial <c>pactl get-sink-volume</c> output.</param>
    /// <returns>The percentage from 0 to 100, or <see langword="null"/> when none is present.</returns>
    public static int? ParsePercent(string? output)
    {
        if (string.IsNullOrEmpty(output))
            return null;

        for (var index = 0; index < output.Length; index++)
        {
            if (output[index] != '%')
                continue;
            var end = index;
            var start = end;
            while (start > 0 && char.IsAsciiDigit(output[start - 1]))
                start--;
            if (start == end)
                continue;
            if (int.TryParse(
                    output.AsSpan(start, end - start),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var percent) &&
                percent is >= 0 and <= 100)
            {
                return percent;
            }
        }
        return null;
    }

    /// <summary>Reads the muted state from a sink-mute response.</summary>
    /// <param name="output">Complete or partial <c>pactl get-sink-mute</c> output.</param>
    /// <returns><see langword="true"/> only when the output explicitly reports <c>Mute: yes</c>.</returns>
    public static bool ParseMuted(string? output) =>
        output?.Contains("Mute: yes", StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>Returns whether a subscription line reports a sink event.</summary>
    /// <param name="line">One line emitted by <c>pactl subscribe</c>.</param>
    /// <returns><see langword="true"/> for sink creation, removal, or change events.</returns>
    public static bool IsSinkEvent(string? line) =>
        line?.Contains(" on sink #", StringComparison.Ordinal) == true;
}
