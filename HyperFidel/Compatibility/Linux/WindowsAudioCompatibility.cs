using HyperFidel.Localization;

namespace HyperFidel.Audio;

/// <summary>
/// Provides platform OpenAL devices and, on Linux, direct ALSA devices through the shared output-profile UI.
/// </summary>
public static class WasapiDeviceProvider
{
    /// <summary>Returns OpenAL devices, the system-default route, and Linux direct ALSA hardware.</summary>
    /// <returns>Available non-Windows audio devices.</returns>
    public static IReadOnlyList<WasapiDeviceInfo> GetRenderDevices()
    {
        var devices = new List<WasapiDeviceInfo>
        {
            new("default", LocalizationManager.Current.LinuxDefaultAudioDevice)
        };
        if (OperatingSystem.IsLinux())
            devices.AddRange(GetDirectAlsaDevices());
        devices.AddRange(
            OpenAlNative.GetOutputDeviceNames()
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .Select(name => new WasapiDeviceInfo(name, name)));
        return devices;
    }

    /// <summary>Returns the system-default OpenAL output route.</summary>
    /// <returns>The default non-Windows audio device.</returns>
    public static WasapiDeviceInfo? GetDefaultRenderDevice() =>
        new("default", LocalizationManager.Current.LinuxDefaultAudioDevice);

    /// <summary>Returns the PCM formats supported by HyperFidel's non-Windows output paths.</summary>
    /// <param name="id">Direct ALSA or OpenAL device identifier.</param>
    /// <returns>PCM output capabilities for the current platform.</returns>
    public static WasapiDeviceCapabilities GetCapabilities(string id)
    {
        var isAlsa = id.StartsWith("alsa:", StringComparison.Ordinal);
        var sampleRates = isAlsa ? GetAlsaSampleRates(id) : [];
        if (sampleRates.Count == 0)
            sampleRates = [44_100, 48_000, 88_200, 96_000, 176_400, 192_000];
        return new(
            ResolveDisplayName(id),
            id,
            isAlsa ? 96_000 : 48_000,
            2,
            isAlsa ? 32 : 16,
            sampleRates,
            [isAlsa ? "S32_LE · 2 ch" : "S16_LE · 2 ch"]);
    }

    /// <summary>
    /// Reads the PCM sample rates a direct ALSA device advertises. The kernel exposes them in
    /// <c>/proc/asound/cardN/streamM</c>; a hard-coded list would hide rates such as 384 kHz.
    /// </summary>
    /// <param name="id">Direct ALSA identifier, for example <c>alsa:hw:CARD=Audio,DEV=0</c>.</param>
    /// <returns>Ascending supported rates, or an empty list when the device cannot be inspected.</returns>
    private static IReadOnlyList<int> GetAlsaSampleRates(string id)
    {
        var name = id["alsa:".Length..];
        var cardId = ReadAlsaParameter(name, "CARD=");
        if (cardId is null)
            return [];
        _ = int.TryParse(ReadAlsaParameter(name, "DEV="), out var deviceNumber);

        var cardNumber = FindCardNumber(cardId);
        if (cardNumber < 0)
            return [];

        var streamPath = $"/proc/asound/card{cardNumber}/stream{deviceNumber}";
        if (!File.Exists(streamPath))
            streamPath = $"/proc/asound/card{cardNumber}/stream0";
        if (!File.Exists(streamPath))
            return [];

        return AlsaCapabilityParser.ParseRateLines(File.ReadLines(streamPath));
    }

    private static string? ReadAlsaParameter(string name, string key)
    {
        var index = name.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
            return null;
        var start = index + key.Length;
        var end = name.IndexOf(',', start);
        var value = (end < 0 ? name[start..] : name[start..end]).Trim();
        return value.Length == 0 ? null : value;
    }

    private static int FindCardNumber(string cardId)
    {
        for (var number = 0; number < 32; number++)
        {
            var idPath = $"/sys/class/sound/card{number}/id";
            if (File.Exists(idPath) &&
                string.Equals(File.ReadAllText(idPath).Trim(), cardId, StringComparison.OrdinalIgnoreCase))
                return number;
        }
        return -1;
    }

    private static string ResolveDisplayName(string id) =>
        string.Equals(id, "default", StringComparison.OrdinalIgnoreCase)
            ? LocalizationManager.Current.LinuxDefaultAudioDevice
            : GetRenderDevices().FirstOrDefault(device => device.Id == id)?.Name ?? id;

    private static IReadOnlyList<WasapiDeviceInfo> GetDirectAlsaDevices()
    {
        const string pcmListPath = "/proc/asound/pcm";
        if (!File.Exists(pcmListPath))
            return [];

        var devices = new List<WasapiDeviceInfo>();
        foreach (var line in File.ReadLines(pcmListPath))
        {
            if (!line.Contains("playback", StringComparison.OrdinalIgnoreCase))
                continue;
            var separator = line.IndexOf(':');
            if (separator < 5 ||
                !int.TryParse(line.AsSpan(0, 2), out var cardNumber) ||
                !int.TryParse(line.AsSpan(3, 2), out var deviceNumber))
                continue;

            var cardIdPath = $"/sys/class/sound/card{cardNumber}/id";
            if (!File.Exists(cardIdPath))
                continue;
            var cardId = File.ReadAllText(cardIdPath).Trim();
            if (cardId.Length == 0)
                continue;

            var fields = line[(separator + 1)..].Split(':', StringSplitOptions.TrimEntries);
            var displayName = fields.FirstOrDefault(static value => value.Length > 0) ?? cardId;
            var alsaName = $"hw:CARD={cardId},DEV={deviceNumber}";
            devices.Add(new($"alsa:{alsaName}", $"ALSA · {displayName} ({cardId})"));
        }
        return devices;
    }
}

/// <summary>
/// Inert endpoint-volume synchronizer. HyperFidel never changes the system
/// (PipeWire/PulseAudio) sink volume: the transport slider is applied only as
/// in-app software gain, and bit-perfect DSD/DoP volume is handled by the
/// hardware mixer through <see cref="AlsaHardwareVolume"/>. This type remains
/// only so the shared window code compiles on every platform.
/// </summary>
internal sealed class WindowsEndpointVolumeSynchronizer : IDisposable
{
    /// <summary>Creates an inert synchronizer that owns no system resource.</summary>
    /// <param name="deviceId">Unused output device identifier.</param>
    internal WindowsEndpointVolumeSynchronizer(string deviceId) { }

    /// <summary>Always reports that endpoint volume is unsupported.</summary>
    /// <param name="deviceId">Unused output device identifier.</param>
    /// <returns>Always <see langword="false"/>.</returns>
    internal static bool SupportsEndpointVolume(string deviceId) => false;

    /// <summary>Gets that the endpoint is never part of the audio path.</summary>
    internal bool AppliesToOutputPath => false;

    /// <summary>Never raised, because the inert synchronizer owns no subscription.</summary>
    internal event EventHandler<float>? VolumeChanged;

    /// <summary>Gets the neutral full-scale volume.</summary>
    internal float Volume => 1.0f;

    /// <summary>Ignores the requested volume; the system sink is never written.</summary>
    /// <param name="volume">Unused linear volume.</param>
    internal void SetVolume(float volume) { }

    /// <inheritdoc/>
    public void Dispose() { }
}
