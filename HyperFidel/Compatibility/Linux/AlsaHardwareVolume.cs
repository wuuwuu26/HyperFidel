using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace HyperFidel.Audio;

/// <summary>
/// Sets a card's ALSA playback mixer to unity before direct hardware output.
/// Direct ALSA bypasses PipeWire/PulseAudio, so a hardware control left at a
/// low value by the system mixer would otherwise cap HyperFidel's software volume.
/// </summary>
internal static partial class AlsaHardwareVolume
{
    private const string Library = "libasound.so.2";

    [DllImport(Library, EntryPoint = "snd_mixer_open", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerOpen(out nint mixer, int mode);

    [DllImport(Library, EntryPoint = "snd_mixer_attach", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerAttach(
        nint mixer,
        [MarshalAs(UnmanagedType.LPStr)] string name);

    [DllImport(Library, EntryPoint = "snd_mixer_selem_register", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerSelemRegister(nint mixer, nint options, nint classp);

    [DllImport(Library, EntryPoint = "snd_mixer_load", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerLoad(nint mixer);

    [DllImport(Library, EntryPoint = "snd_mixer_first_elem", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SndMixerFirstElem(nint mixer);

    [DllImport(Library, EntryPoint = "snd_mixer_elem_next", CallingConvention = CallingConvention.Cdecl)]
    private static extern nint SndMixerElemNext(nint element);

    [DllImport(Library, EntryPoint = "snd_mixer_selem_has_playback_volume", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerSelemHasPlaybackVolume(nint element);

    [DllImport(Library, EntryPoint = "snd_mixer_selem_get_playback_volume_range", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerSelemGetPlaybackVolumeRange(
        nint element,
        out long minimum,
        out long maximum);

    [DllImport(Library, EntryPoint = "snd_mixer_selem_set_playback_volume_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerSelemSetPlaybackVolumeAll(nint element, long value);

    [DllImport(Library, EntryPoint = "snd_mixer_selem_has_playback_switch", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerSelemHasPlaybackSwitch(nint element);

    [DllImport(Library, EntryPoint = "snd_mixer_selem_set_playback_switch_all", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerSelemSetPlaybackSwitchAll(nint element, int value);

    [DllImport(Library, EntryPoint = "snd_mixer_close", CallingConvention = CallingConvention.Cdecl)]
    private static extern int SndMixerClose(nint mixer);

    /// <summary>Sets every playback volume control on the card to its maximum and unmutes it.</summary>
    /// <param name="alsaDeviceId">Direct ALSA identifier such as <c>alsa:hw:CARD=Audio,DEV=0</c>.</param>
    internal static void EnsureUnity(string alsaDeviceId)
    {
        if (!OperatingSystem.IsLinux())
            return;
        var cardId = ExtractCardId(alsaDeviceId);
        if (cardId is null)
            return;
        var mixer = Open(cardId);
        if (mixer == nint.Zero)
            return;
        try
        {
            SetPlaybackVolume(mixer, 1.0f);
        }
        catch
        {
            // A missing or restricted mixer must never prevent playback.
        }
        finally
        {
            SndMixerClose(mixer);
        }
    }

    /// <summary>Sets every playback volume control on the card to a linear fraction of its range.</summary>
    /// <param name="alsaDeviceId">Direct ALSA identifier such as <c>alsa:hw:CARD=Audio,DEV=0</c>.</param>
    /// <param name="volume">Linear volume from 0.0 to 1.0.</param>
    internal static void SetVolume(string alsaDeviceId, float volume)
    {
        if (!OperatingSystem.IsLinux())
            return;
        var cardId = ExtractCardId(alsaDeviceId);
        if (cardId is null)
            return;
        var mixer = Open(cardId);
        if (mixer == nint.Zero)
            return;
        try
        {
            SetPlaybackVolume(mixer, Math.Clamp(volume, 0.0f, 1.0f));
        }
        catch
        {
            // Bit-perfect DSD falls back to the hardware mixer; a missing mixer
            // must never interrupt playback.
        }
        finally
        {
            SndMixerClose(mixer);
        }
    }

    private static nint Open(string cardId)
    {
        if (SndMixerOpen(out var mixer, 0) < 0)
            return nint.Zero;
        try
        {
            if (SndMixerAttach(mixer, $"hw:CARD={cardId}") < 0 ||
                SndMixerSelemRegister(mixer, nint.Zero, nint.Zero) < 0 ||
                SndMixerLoad(mixer) < 0)
            {
                SndMixerClose(mixer);
                return nint.Zero;
            }
            return mixer;
        }
        catch
        {
            SndMixerClose(mixer);
            return nint.Zero;
        }
    }

    private static void SetPlaybackVolume(nint mixer, float volume)
    {
        for (var element = SndMixerFirstElem(mixer);
             element != nint.Zero;
             element = SndMixerElemNext(element))
        {
            if (SndMixerSelemHasPlaybackVolume(element) == 0)
                continue;
            if (SndMixerSelemGetPlaybackVolumeRange(element, out var minimum, out var maximum) < 0 ||
                maximum <= minimum)
            {
                continue;
            }
            var value = minimum +
                        (long)Math.Round((maximum - minimum) * (double)volume);
            _ = SndMixerSelemSetPlaybackVolumeAll(element, value);
            if (SndMixerSelemHasPlaybackSwitch(element) != 0)
                _ = SndMixerSelemSetPlaybackSwitchAll(element, volume > 0.0f ? 1 : 0);
        }
    }

    private static string? ExtractCardId(string deviceId)
    {
        var match = CardIdRegex().Match(deviceId);
        return match.Success ? match.Groups[1].Value : null;
    }

    [GeneratedRegex(@"CARD=([^,\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CardIdRegex();
}
