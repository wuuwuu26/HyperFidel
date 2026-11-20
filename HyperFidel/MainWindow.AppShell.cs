using System.IO;
using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Reactive;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls.Primitives;
using Avalonia.Styling;
using AvaloniaEllipse = Avalonia.Controls.Shapes.Ellipse;
using AvaloniaPath = Avalonia.Controls.Shapes.Path;
using HyperFidel.Audio;
using HyperFidel.Controls;
using HyperFidel.Library;
using HyperFidel.Localization;
using HyperFidel.Streaming;
using Windows.Media;

namespace HyperFidel;

/// <summary>
/// Display formatting, server-settings equality, library-import preparation, and About.
/// </summary>
public partial class MainWindow : Window
{
    private static string GetContentColumnWidthKey(string view) =>
        view.StartsWith("Playlist:", StringComparison.Ordinal)
            ? "Content.Playlist"
            : $"Content.{view}";

    private static string FormatTime(TimeSpan value) =>
        value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"m\:ss");



    /// <summary>Invalidates catalog snapshots and abandons pending catalog, search, playlist and playback work before replacing the local library.</summary>
    internal void PrepareForLibraryImport()
    {
        CancelLibrarySearch();
        InvalidateCatalogConfiguration();
        CancelAndDispose(ref _unifiedLibraryAppendCts);
        ClearUnifiedLibraryLoadNotice();
        _libraryWatcher?.Dispose();
        _libraryWatcher = null;
        StopPlayback();
    }

    private async void AboutButton_OnClick(object? sender, RoutedEventArgs e)
    {
        CloseEmbeddedSettings();
        await new AboutWindow().ShowDialog<object?>(this);
    }
}
