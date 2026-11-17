using Avalonia.Controls;
using HyperFidel.Localization;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HyperFidel;

public partial class AboutWindow : Window
{
    private readonly string _version;

    /// <summary>Initializes the About window and displays the build-time release version.</summary>
    public AboutWindow()
    {
        InitializeComponent();
        var assembly = typeof(AboutWindow).Assembly;
        _version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? assembly.GetName().Version?.ToString(3) ?? "0.0.0";
        VersionTextBlock.Text = string.Format(LocalizationManager.Current.VersionLabel, _version);
        BuildInfoTextBlock.Text = string.Join(
            " · ",
            RuntimeInformation.FrameworkDescription,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString());
        Opened += (_, _) => WindowChrome.ApplyTheme(this);
    }

    private void OpenLinkButton_OnClick(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string url } ||
            !Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
        }
    }
}
