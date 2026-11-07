using System.Diagnostics;
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

#if HYPERFIDEL_LINUX
/// <summary>
/// Mirrors the PipeWire/PulseAudio default sink volume while HyperFidel owns a
/// direct ALSA device, so desktop volume keys and the on-screen display still
/// control playback that bypasses the system mixer.
/// </summary>
internal sealed class WindowsEndpointVolumeSynchronizer : IDisposable
{
    private const string DefaultSink = "@DEFAULT_SINK@";
    private readonly object _gate = new();
    private readonly object _applyGate = new();
    private readonly CancellationTokenSource _cancellation = new();
    private Process? _subscription;
    private int _pendingPercent = -1;
    private int _lastAppliedPercent = -1;
    private int _lastKnownPercent;
    private int _lastKnownMuted;
    private bool _applying;
    private bool _disposed;

    /// <summary>Starts mirroring the default sink volume for one direct ALSA device.</summary>
    /// <param name="deviceId">Selected output device identifier.</param>
    /// <exception cref="PlatformNotSupportedException">
    /// Thrown when the device is not direct ALSA, <c>pactl</c> is unavailable,
    /// or the current sink volume cannot be read.
    /// </exception>
    internal WindowsEndpointVolumeSynchronizer(string deviceId)
    {
        if (!SupportsEndpointVolume(deviceId) || !IsPactlAvailable())
            throw new PlatformNotSupportedException();
        var state = ReadState();
        if (state is null)
            throw new PlatformNotSupportedException();
        _lastKnownPercent = state.Value.Percent;
        _lastKnownMuted = state.Value.Muted ? 1 : 0;
        _ = Task.Run(SubscribeAsync);
    }

    /// <summary>Gets whether a device identifier can be mirrored through the system sink.</summary>
    /// <param name="deviceId">Selected output device identifier.</param>
    /// <returns><see langword="true"/> for a Linux direct ALSA device.</returns>
    internal static bool SupportsEndpointVolume(string deviceId) =>
        OperatingSystem.IsLinux() &&
        deviceId.StartsWith("alsa:", StringComparison.Ordinal);

    /// <summary>Gets whether the endpoint volume is already part of the audio path.</summary>
    /// <remarks>
    /// Direct ALSA bypasses the system mixer, so the player still applies the
    /// mirrored gain itself instead of relying on the endpoint.
    /// </remarks>
    internal bool AppliesToOutputPath => false;

    /// <summary>Raised when the system default sink volume or mute state changes.</summary>
    internal event EventHandler<float>? VolumeChanged;

    /// <summary>Gets the last observed system volume from 0.0 to 1.0.</summary>
    internal float Volume =>
        Volatile.Read(ref _lastKnownMuted) != 0
            ? 0.0f
            : Volatile.Read(ref _lastKnownPercent) / 100.0f;

    /// <summary>Sets the system default sink volume from 0.0 to 1.0.</summary>
    /// <param name="volume">Linear volume.</param>
    internal void SetVolume(float volume)
    {
        var percent = (int)Math.Round(Math.Clamp(volume, 0.0f, 1.0f) * 100.0);
        lock (_applyGate)
        {
            _pendingPercent = percent;
            if (_applying)
                return;
            _applying = true;
        }
        _ = Task.Run(ApplyPendingVolumeAsync);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        _cancellation.Cancel();
        Process? subscription;
        lock (_gate)
        {
            subscription = _subscription;
            _subscription = null;
        }
        try
        {
            subscription?.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        subscription?.Dispose();
    }

    private async Task ApplyPendingVolumeAsync()
    {
        while (true)
        {
            int percent;
            lock (_applyGate)
            {
                percent = _pendingPercent;
                _pendingPercent = -1;
                if (percent < 0)
                {
                    _applying = false;
                    return;
                }
            }
            ApplyVolume(percent);
        }
    }

    private void ApplyVolume(int percent)
    {
        if (RunPactl("set-sink-volume", DefaultSink, $"{percent}%") is null)
            return;
        Volatile.Write(ref _lastAppliedPercent, percent);
        if (percent > 0)
            RunPactl("set-sink-mute", DefaultSink, "0");
    }

    private async Task SubscribeAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                using var process = StartSubscription();
                if (process is null)
                    return;
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    _subscription = process;
                }
                int? lastPercent = null;
                bool? lastMuted = null;
                while (!_cancellation.IsCancellationRequested)
                {
                    var line = await process.StandardOutput
                        .ReadLineAsync(_cancellation.Token)
                        .ConfigureAwait(false);
                    if (line is null)
                        break;
                    if (!PactlVolumeOutput.IsSinkEvent(line))
                        continue;
                    var state = ReadState();
                    if (state is null)
                        continue;
                    if (state.Value.Percent == lastPercent && state.Value.Muted == lastMuted)
                        continue;
                    lastPercent = state.Value.Percent;
                    lastMuted = state.Value.Muted;
                    Volatile.Write(ref _lastKnownPercent, state.Value.Percent);
                    Volatile.Write(ref _lastKnownMuted, state.Value.Muted ? 1 : 0);
                    if (!state.Value.Muted &&
                        state.Value.Percent == Volatile.Read(ref _lastAppliedPercent))
                    {
                        continue;
                    }
                    VolumeChanged?.Invoke(
                        this,
                        state.Value.Muted ? 0.0f : state.Value.Percent / 100.0f);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch
            {
            }
            finally
            {
                lock (_gate)
                {
                    _subscription = null;
                }
            }
            try
            {
                await Task.Delay(1000, _cancellation.Token).ConfigureAwait(false);
            }
            catch
            {
                return;
            }
        }
    }

    private static (int Percent, bool Muted)? ReadState()
    {
        var volumeOutput = RunPactl("get-sink-volume", DefaultSink);
        var percent = PactlVolumeOutput.ParsePercent(volumeOutput);
        if (percent is null)
            return null;
        var muted = PactlVolumeOutput.ParseMuted(
            RunPactl("get-sink-mute", DefaultSink));
        return (percent.Value, muted);
    }

    private static bool IsPactlAvailable() => HasExecutable("pactl");

    private static bool HasExecutable(string name)
    {
        var path = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(path))
            return false;
        return path
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(directory => File.Exists(Path.Combine(directory, name)));
    }

    /// <summary>
    /// Starts <c>pactl subscribe</c> behind a parent-death watchdog so the
    /// long-running subscription cannot outlive HyperFidel, even when the process
    /// is terminated without running its normal shutdown path.
    /// </summary>
    /// <returns>The running watchdog shell, or <see langword="null"/> when it cannot be started.</returns>
    private static Process? StartSubscription()
    {
        // pactl fully buffers its stdout when it is a pipe. Without line
        // buffering the subscription would only deliver events when the buffer
        // fills or the process exits, so the mirrored volume would appear stuck.
        var subscribe = HasExecutable("stdbuf")
            ? "stdbuf -oL pactl subscribe"
            : "pactl subscribe";
        var script =
            $"{subscribe} 2>/dev/null & child=$!; " +
            $"tail --pid={Environment.ProcessId} -f /dev/null; " +
            "kill $child 2>/dev/null";
        try
        {
            var startInfo = new ProcessStartInfo("/bin/sh")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-c");
            startInfo.ArgumentList.Add(script);
            startInfo.Environment["LC_ALL"] = "C";
            startInfo.Environment["LANG"] = "C";
            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    private static Process? StartPactl(params string[] arguments)
    {
        try
        {
            var startInfo = new ProcessStartInfo("pactl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);
            startInfo.Environment["LC_ALL"] = "C";
            startInfo.Environment["LANG"] = "C";
            return Process.Start(startInfo);
        }
        catch
        {
            return null;
        }
    }

    private static string? RunPactl(params string[] arguments)
    {
        try
        {
            using var process = StartPactl(arguments);
            if (process is null)
                return null;
            var error = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd();
            _ = error.Wait(2000);
            if (!process.WaitForExit(2000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                return null;
            }
            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
#else
/// <summary>
/// Placeholder for Windows endpoint-volume synchronization on platforms without
/// a supported system mixer.
/// </summary>
internal sealed class WindowsEndpointVolumeSynchronizer : IDisposable
{
    /// <summary>Rejects creation because endpoint volume is unavailable.</summary>
    /// <param name="deviceId">Unused device identifier.</param>
    /// <exception cref="PlatformNotSupportedException">Always thrown.</exception>
    internal WindowsEndpointVolumeSynchronizer(string deviceId) =>
        throw new PlatformNotSupportedException();

    /// <summary>Gets whether a device identifier can be mirrored through the system sink.</summary>
    /// <param name="deviceId">Selected output device identifier.</param>
    /// <returns>Always <see langword="false"/> on this platform.</returns>
    internal static bool SupportsEndpointVolume(string deviceId) => false;

    /// <summary>Gets whether the endpoint volume is already part of the audio path.</summary>
    internal bool AppliesToOutputPath => false;

    /// <summary>Would be raised when a platform endpoint changes volume.</summary>
    internal event EventHandler<float>? VolumeChanged;

    /// <summary>Gets a neutral volume value.</summary>
    internal float Volume => 1.0f;

    /// <summary>Ignores a volume update.</summary>
    /// <param name="volume">Requested linear volume.</param>
    internal void SetVolume(float volume) { }

    /// <inheritdoc/>
    public void Dispose() { }
}
#endif
