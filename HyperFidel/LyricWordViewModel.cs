using System.ComponentModel;

namespace HyperFidel;

/// <summary>
/// One word of an enhanced-LRC lyric line. The lyrics view uses it to light words up
/// as their timestamps pass, which makes the line progress visible between line changes.
/// </summary>
internal sealed class LyricWordViewModel : INotifyPropertyChanged
{
    private bool _isSung;

    /// <summary>Creates a timed lyric word.</summary>
    /// <param name="text">Display text of the word.</param>
    /// <param name="time">Timestamp at which the word becomes audible.</param>
    public LyricWordViewModel(string text, TimeSpan time)
    {
        Text = text;
        Time = time;
    }

    /// <summary>Gets the display text of the word.</summary>
    public string Text { get; }

    /// <summary>Gets the timestamp at which the word becomes audible.</summary>
    public TimeSpan Time { get; }

    /// <summary>Gets or sets a value indicating whether playback has reached the word.</summary>
    public bool IsSung
    {
        get => _isSung;
        set
        {
            if (_isSung == value)
                return;
            _isSung = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSung)));
        }
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;
}
