using HyperFidel.Audio;
using Xunit;

namespace HyperFidel.Tests;

/// <summary>
/// Guards the offline-player contract that HyperFidel never changes the system
/// (PipeWire/PulseAudio) sink volume or the ALSA hardware mixer for PCM output.
/// Only bit-perfect DSD/DoP players are allowed to write the hardware mixer.
/// </summary>
public sealed class NoSystemVolumeWritesTests
{
    /// <summary>
    /// Locates a repository file by walking up from the test output directory, so
    /// the static source audit works both locally and in CI.
    /// </summary>
    private static string FindRepoFile(string relativePath)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory);
             directory is not null;
             directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }

        throw new FileNotFoundException(relativePath);
    }

    /// <summary>
    /// The Linux endpoint synchronizer must be disabled so the window never
    /// creates it and never writes the default sink volume through pactl.
    /// </summary>
    [Fact]
    public void LinuxEndpointSynchronizer_IsDisabledForDirectAlsa()
    {
        Assert.False(WindowsEndpointVolumeSynchronizer.SupportsEndpointVolume(
            "alsa:hw:CARD=USB,DEV=0"));
    }

    /// <summary>
    /// The PCM open path must not force the ALSA hardware mixer to unity; that
    /// push-to-maximum was the cause of the system volume jumping to 100 %.
    /// </summary>
    [Fact]
    public void PcmOpenPath_DoesNotForceHardwareMixerToUnity()
    {
        var source = FindRepoFile(
            Path.Combine("HyperFidel", "Compatibility", "Linux", "WasapiAudioPlayer.cs"));

        Assert.DoesNotContain("EnsureUnity", source);
    }

    /// <summary>
    /// No Linux compatibility code may write the system sink volume or mute flag.
    /// </summary>
    [Fact]
    public void LinuxCompatibility_DoesNotWriteSystemSinkVolume()
    {
        var source = FindRepoFile(
            Path.Combine("HyperFidel", "Compatibility", "Linux", "WindowsAudioCompatibility.cs"));

        Assert.DoesNotContain("set-sink-volume", source);
        Assert.DoesNotContain("set-sink-mute", source);
    }
}
