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
/// MusicBrainz community rating column for the shared track tables.
/// </summary>
public partial class MainWindow : Window
{
    /// <summary>Creates the optional cached MusicBrainz rating column with an explicit refresh action.</summary>
    /// <returns>A community-rating column.</returns>
    private DataGridColumn CreateMusicBrainzRatingColumn()
    {
        var column = new DataGridTemplateColumn
        {
            Header = LocalizationManager.Current.MusicBrainzRating,
            SortMemberPath = nameof(ContentRow.MusicBrainzRating),
            Width = new DataGridLength(160),
            CellTemplate = new FuncDataTemplate<ContentRow>((row, _) =>
            {
                var button = new Button
                {
                    Theme = FindResource<ControlTheme>("EntityLinkButtonTheme"),
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                button.Bind(Button.ContentProperty, new Binding(nameof(ContentRow.MusicBrainzRatingDisplay)));
                ToolTip.SetTip(button, LocalizationManager.Current.MusicBrainzRating);
                button.Click += async (_, e) =>
                {
                    e.Handled = true;
                    await RefreshMusicBrainzTrackRatingForegroundAsync(row);
                };
                return button;
            })
        };
        column.Tag = "musicBrainzRating";
        return column;
    }
}
