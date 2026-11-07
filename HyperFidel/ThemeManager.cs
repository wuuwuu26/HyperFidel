using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using AvaloniaApp = Avalonia.Application;

namespace HyperFidel;

/// <summary>
/// Applies a light or dark colour scheme by writing named <see cref="SolidColorBrush"/> resources
/// into <see cref="Avalonia.Application.Current"/> resources so all controls pick them up dynamically.
/// </summary>
public static class ThemeManager
{
    /// <summary>Switches the global Avalonia resource dictionary to the specified theme.</summary>
    /// <param name="theme">The theme whose brushes and image assets should be applied.</param>
    public static void Apply(AppTheme theme)
    {
        var app = AvaloniaApp.Current!;
        var resources = app.Resources;
        var dark = theme == AppTheme.Dark;

        // Align the built-in Fluent theme variant with the selected scheme so that
        // any control still using default Fluent resources (e.g. code-created
        // checkboxes) resolves dark-appropriate colours instead of light-variant
        // borders that would appear near-black on the dark background.
        app.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;

        resources["AppAccentBrush"]              = Brush(dark ? "#E8C46A" : "#A67C00");
        resources["AppFavoriteBrush"]            = Brush(dark ? "#F0875D" : "#C2410C");
        resources["AppAccentTextBrush"]          = Brush(dark ? "#1A1408" : "#FFFFFF");
        resources["AppAccentSoftBrush"]          = Brush(dark ? "#3A2E14" : "#FBF1D6");
        resources["AppSidebarBrush"]             = Brush(dark ? "#E60D0B07" : "#FAF6EC");
        resources["AppHeaderBrush"]              = Brush(dark ? "#E6100D08" : "#FFFFFF");
        resources["AppHeaderBorderBrush"]        = Brush(dark ? "#2A2314" : "#E8DFC8");
        resources["AppContentBrush"]             = Brush(dark ? "#D912100A" : "#FDFBF4");
        resources["AppSurfaceBrush"]             = Brush(dark ? "#CC1A1610" : "#FFFFFF");
        resources["AppSurfaceHoverBrush"]        = Brush(dark ? "#D9241E14" : "#FAF4E4");
        resources["AppSurfaceSelectedBrush"]     = Brush(dark ? "#E62E2618" : "#F5EBCF");
        resources["AppNowPlayingRowBrush"]       = Brush(dark ? "#E62B2210" : "#FBF1D6");
        resources["AppNavHoverBrush"]            = Brush(dark ? "#D91A1610" : "#F7F0DF");
        resources["AppNavSelectedBrush"]         = Brush(dark ? "#E62B2210" : "#F3E7C8");
        resources["AppNavTextBrush"]             = Brush(dark ? "#9A9078" : "#7A6A45");
        resources["AppNavHoverTextBrush"]        = Brush(dark ? "#E6DCC4" : "#2A2418");
        resources["AppNavSelectedTextBrush"]     = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["AppPrimaryTextBrush"]         = Brush(dark ? "#F6F0E2" : "#2A2418");
        resources["AppSecondaryTextBrush"]       = Brush(dark ? "#C6BBA2" : "#6A5E44");
        resources["AppMutedTextBrush"]           = Brush(dark ? "#8A8070" : "#8A7E63");
        resources["AppGridLineBrush"]            = Brush(dark ? "#2A2416" : "#E8DFC8");
        resources["AppColumnHeaderBrush"]        = Brush(dark ? "#E616130C" : "#F7F2E4");
        resources["AppTransportBrush"]           = Brush(dark ? "#F20C0A06" : "#FFFFFF");
        resources["AppTransportBorderBrush"]     = Brush(dark ? "#2A2314" : "#E8DFC8");
        resources["AppTransportPrimaryTextBrush"] = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["AppTransportButtonBrush"]     = Brush(dark ? "#D91A1610" : "#F7F0DF");
        resources["AppTransportButtonHoverBrush"] = Brush(dark ? "#E6241E14" : "#EFE4C8");
        resources["AppTransportButtonDisabledBrush"] = Brush(dark ? "#CC14110B" : "#FAF6EC");
        resources["AppTransportButtonTextBrush"] = Brush(dark ? "#D8CCB2" : "#5A4E36");
        resources["AppTransportButtonHoverTextBrush"] = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["AppTransportButtonDisabledTextBrush"] = Brush(dark ? "#4A4232" : "#B8AC90");
        resources["AppTransportPlayBrush"]       = Brush(dark ? "#E8C46A" : "#A67C00");
        resources["AppTransportPlayHoverBrush"]  = Brush(dark ? "#F2D98C" : "#8A6600");
        resources["AppTransportPlayDisabledBrush"] = Brush(dark ? "#E6241E14" : "#EFE4C8");
        resources["AppTransportPlayDisabledTextBrush"] = Brush(dark ? "#4A4232" : "#B8AC90");
        resources["AppArtworkPlaceholderBrush"]  = Brush(dark ? "#CC201B12" : "#EDE4CF");
        resources["AppContentWellBrush"]         = Gradient(dark ? "#060504" : "#F1EADA", dark ? "#15120B" : "#FDFBF4");
        resources["AppHeaderSurfaceBrush"]       = Gradient(dark ? "#1B1610" : "#FFFFFF", dark ? "#0E0B07" : "#F4EEDF");
        resources["AppTransportSurfaceBrush"]    = Gradient(dark ? "#18130C" : "#FFFFFF", dark ? "#090703" : "#F1EADA");
        resources["AppInsetBrush"]               = Gradient(dark ? "#080604" : "#F2ECDE", dark ? "#18140C" : "#FBF7EC");
        resources["AppRaisedBrush"]              = Gradient(dark ? "#241E14" : "#FFFFFF", dark ? "#15110B" : "#F6F0E2");
        resources["AppArtworkEdgeBrush"]         = Brush(dark ? "#33FFFFFF" : "#2E000000");
        resources["AppBevelHighlightBrush"]      = Brush(dark ? "#33FFFFFF" : "#66FFFFFF");
        resources["AppScrollbarTrackBrush"]      = Brush(dark ? "#CC14110B" : "#F5EFE0");
        resources["AppScrollbarThumbBrush"]      = Brush(dark ? "#3A3222" : "#CBBE9E");
        resources["AppScrollbarThumbHoverBrush"] = Brush(dark ? "#4E442E" : "#B3A47F");
        resources["AppButtonBrush"]              = Brush(dark ? "#D91A1610" : "#F7F0DF");
        resources["AppButtonHoverBrush"]         = Brush(dark ? "#E6241E14" : "#EFE4C8");
        resources["AppButtonPressedBrush"]       = Brush(dark ? "#E62E2618" : "#E6D8B8");
        resources["AppButtonTextBrush"]          = Brush(dark ? "#F6F0E2" : "#2A2418");
        resources["AppButtonBorderBrush"]        = Brush(dark ? "#3A3222" : "#D8CBA8");
        resources["AppInputBrush"]               = Brush(dark ? "#CC16130C" : "#FFFFFF");
        resources["AppInputBorderBrush"]         = Brush(dark ? "#3A3222" : "#D8CBA8");
        resources["TextControlBackground"]       = Brush(dark ? "#CC16130C" : "#FFFFFF");
        resources["TextControlBackgroundPointerOver"] = Brush(dark ? "#D91A1610" : "#FFFFFF");
        resources["TextControlBackgroundFocused"] = Brush(dark ? "#E61A1610" : "#FFFFFF");
        resources["TextControlForeground"]       = Brush(dark ? "#F6F0E2" : "#2A2418");
        resources["TextControlForegroundPointerOver"] = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["TextControlForegroundFocused"] = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["TextControlBorderBrush"]      = Brush(dark ? "#3A3222" : "#D8CBA8");
        resources["TextControlBorderBrushPointerOver"] = Brush(dark ? "#4E442E" : "#C0B08A");
        resources["TextControlBorderBrushFocused"] = Brush(dark ? "#E8C46A" : "#A67C00");
        resources["TextControlPlaceholderForeground"] = Brush(dark ? "#8A8070" : "#8A7E63");
        resources["TextControlPlaceholderForegroundPointerOver"] = Brush(dark ? "#8A8070" : "#8A7E63");
        resources["TextControlPlaceholderForegroundFocused"] = Brush(dark ? "#8A8070" : "#8A7E63");
        resources["AppDsdSupportedBrush"]        = Brush(dark ? "#F6F0E2" : "#2A2418");
        resources["AppDsdUnsupportedBrush"]      = Brush(dark ? "#6A6152" : "#B8AC90");
        resources["TreeViewItemForeground"] = Brush(dark ? "#C6BBA2" : "#6A5E44");
        resources["TreeViewItemForegroundPointerOver"] = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["TreeViewItemForegroundPressed"] = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["TreeViewItemForegroundSelected"] = Brush(dark ? "#FFF6E0" : "#2A2418");
        resources["TreeViewItemBackgroundSelected"] = Brush(dark ? "#E62E2618" : "#F5EBCF");
        resources["TreeViewItemBackgroundPointerOver"] = Brush(dark ? "#D9241E14" : "#FAF4E4");

        resources["AppAboutLogoSource"] = AssetImage(dark
            ? "avares://HyperFidel/Assets/HyperFidel_Logo.png"
            : "avares://HyperFidel/Assets/hyperfidel_about_light.png");
        resources["AppProgramLogoSource"] = AssetImage(dark
            ? "avares://HyperFidel/Assets/HyperFidel_Logo.png"
            : "avares://HyperFidel/Assets/hyperfidel_about_light.png");
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));

    /// <summary>Creates a vertical two-stop gradient used for raised and recessed surfaces.</summary>
    /// <param name="top">Colour at the top edge.</param>
    /// <param name="bottom">Colour at the bottom edge.</param>
    /// <returns>The configured brush.</returns>
    private static LinearGradientBrush Gradient(string top, string bottom) => new()
    {
        StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
        EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
        GradientStops =
        {
            new GradientStop(Color.Parse(top), 0),
            new GradientStop(Color.Parse(bottom), 1)
        }
    };

    private static Bitmap AssetImage(string uri) => new(AssetLoader.Open(new Uri(uri)));
}
