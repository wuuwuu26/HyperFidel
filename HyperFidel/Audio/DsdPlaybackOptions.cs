namespace HyperFidel.Audio;

/// <summary>
/// Process-wide options for converting DSD sources to PCM. The output rate is resolved in
/// several places that do not receive the user settings (player probes, gapless hand-off and
/// the pre-known-info fast path), so the configured value is published here when settings are
/// loaded or applied.
/// </summary>
public static class DsdPlaybackOptions
{
    /// <summary>Conventional DSD64 conversion target, used when no explicit rate is configured.</summary>
    public const int ConventionalPcmRate = 176_400;

    /// <summary>
    /// Gets or sets the PCM output rate used for DSD sources. Zero selects
    /// <see cref="ConventionalPcmRate"/>; other values are clamped to a sane PCM range.
    /// </summary>
    public static int PcmSampleRateHz { get; set; } = 384_000;

    /// <summary>Returns the PCM output rate to use for a DSD source.</summary>
    /// <returns>A rate between 8 kHz and 768 kHz.</returns>
    public static int ResolveOutputRate() =>
        PcmSampleRateHz is >= 8_000 and <= 768_000 ? PcmSampleRateHz : ConventionalPcmRate;
}
