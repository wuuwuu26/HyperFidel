using NAudio.CoreAudioApi;

namespace HyperFidel.Audio;

/// <summary>
/// Synchronizes application controls with the Windows master volume of one
/// render endpoint.
/// </summary>
internal sealed class WindowsEndpointVolumeSynchronizer : IDisposable
{
    private readonly MMDevice _device;
    private readonly AudioEndpointVolume _endpointVolume;
    private bool _disposed;

    /// <summary>Opens the render endpoint and subscribes to master-volume changes.</summary>
    /// <param name="deviceId">Windows render endpoint identifier.</param>
    internal WindowsEndpointVolumeSynchronizer(string deviceId)
    {
        _device = WasapiDeviceProvider.GetRenderDevice(deviceId);
        _endpointVolume = _device.AudioEndpointVolume;
        _endpointVolume.OnVolumeNotification += EndpointVolume_OnVolumeNotification;
    }

    /// <summary>Gets whether a device identifier can be mirrored through the system endpoint.</summary>
    /// <param name="deviceId">Selected output device identifier.</param>
    /// <returns>Always <see langword="true"/> because every Windows render endpoint exposes master volume.</returns>
    internal static bool SupportsEndpointVolume(string deviceId) => true;

    /// <summary>Gets whether the endpoint volume is already part of the audio path.</summary>
    internal bool AppliesToOutputPath => true;

    /// <summary>Raised when Windows reports a new master-volume value.</summary>
    internal event EventHandler<float>? VolumeChanged;

    /// <summary>Gets the current endpoint master volume from 0.0 to 1.0.</summary>
    internal float Volume => _endpointVolume.MasterVolumeLevelScalar;

    /// <summary>Sets the endpoint master volume from 0.0 to 1.0.</summary>
    /// <param name="volume">Linear endpoint volume.</param>
    internal void SetVolume(float volume) =>
        _endpointVolume.MasterVolumeLevelScalar = Math.Clamp(volume, 0.0f, 1.0f);

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        _endpointVolume.OnVolumeNotification -= EndpointVolume_OnVolumeNotification;
        _device.Dispose();
        _disposed = true;
    }

    private void EndpointVolume_OnVolumeNotification(AudioVolumeNotificationData data) =>
        VolumeChanged?.Invoke(this, data.MasterVolume);
}
