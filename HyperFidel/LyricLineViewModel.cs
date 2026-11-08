using System.ComponentModel;
using HyperFidel.Library;

namespace HyperFidel;

/// <summary>
/// One synchronized lyric line of the lyrics view. It lives outside the main window so
/// XAML can name it in an <c>x:DataType</c> directive, which compiled bindings require
/// (a nested type cannot be referenced from XAML).
/// </summary>
internal sealed class LyricLineViewModel : INotifyPropertyChanged
{
    private readonly LyricWordViewModel[] _wordItems;
    private bool _isActive;

    /// <summary>Creates a lyric line.</summary>
    /// <param name="text">Display text of the line.</param>
    /// <param name="time">Line timestamp, or <see langword="null"/> for an untimed line.</param>
    /// <param name="words">Word-level timings of an enhanced-LRC line.</param>
    public LyricLineViewModel(string text, TimeSpan? time, IReadOnlyList<TimedLyricWord>? words = null)
    {
        Text = text;
        Time = time;
        Words = words ?? [];
        var source = Words;
        _wordItems = new LyricWordViewModel[source.Count];
        for (var index = 0; index < source.Count; index++)
            _wordItems[index] = new LyricWordViewModel(source[index].Text, source[index].Time);
    }

    /// <summary>Gets the display text of the line.</summary>
    public string Text { get; }

    /// <summary>Gets the line timestamp, or <see langword="null"/> for an untimed line.</summary>
    public TimeSpan? Time { get; }

    /// <summary>Gets the word-level timings of an enhanced-LRC line; empty for plain lines.</summary>
    public IReadOnlyList<TimedLyricWord> Words { get; } = [];

    /// <summary>Gets the bindable word items of an enhanced-LRC line.</summary>
    public IReadOnlyList<LyricWordViewModel> WordItems => _wordItems;

    /// <summary>Gets a value indicating whether the line has word-level timings.</summary>
    public bool HasWords => _wordItems.Length > 0;

    /// <summary>Gets or sets a value indicating whether the line is the audible one.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value)
                return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    /// <summary>Marks every word that playback has already reached.</summary>
    /// <param name="position">Current playback position.</param>
    public void UpdateWordProgress(TimeSpan position)
    {
        foreach (var word in _wordItems)
            word.IsSung = position >= word.Time;
    }

    /// <inheritdoc/>
    public event PropertyChangedEventHandler? PropertyChanged;
}
