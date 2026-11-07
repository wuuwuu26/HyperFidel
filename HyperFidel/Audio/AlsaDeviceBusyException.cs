namespace HyperFidel.Audio;

/// <summary>Raised when a requested direct ALSA device is already owned by another process.</summary>
internal sealed class AlsaDeviceBusyException : IOException
{
    /// <summary>Creates the exception with the localized device-busy message.</summary>
    /// <param name="message">Localized diagnostic message.</param>
    public AlsaDeviceBusyException(string message) : base(message)
    {
    }
}
