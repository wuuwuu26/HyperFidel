using Avalonia;
using Avalonia.Media;

namespace HyperFidel;

/// <summary>Configures and starts the HyperFidel Avalonia desktop application.</summary>
sealed class Program
{
    /// <summary>
    /// Holds the single-instance guard for the process lifetime. A second launch
    /// exits instead of running beside the first one, because two instances would
    /// both persist the settings file and fight over the MPRIS media name, which
    /// made a saved volume look like it reset itself.
    /// </summary>
    private static Mutex? SingleInstanceMutex;

    /// <summary>Starts the classic desktop lifetime.</summary>
    /// <param name="args">Command-line arguments passed to Avalonia.</param>
    [STAThread]
    public static void Main(string[] args)
    {
        SingleInstanceMutex = TryAcquireSingleInstance();
        if (SingleInstanceMutex is null)
            return;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    /// <summary>
    /// Acquires the per-user single-instance mutex.
    /// </summary>
    /// <returns>The held mutex, or <see langword="null"/> when another instance owns it.</returns>
    private static Mutex? TryAcquireSingleInstance()
    {
        // Session-scoped name: separate desktop logins may each run their own
        // instance without sharing a data root that belongs to one user.
        var name = OperatingSystem.IsWindows()
            ? @"Local\HyperFidel.Desktop"
            : $"hyperfidel-desktop-{Environment.UserName}";
        var mutex = new Mutex(initiallyOwned: false, name);
        try
        {
            if (mutex.WaitOne(TimeSpan.Zero, exitContext: false))
                return mutex;
        }
        catch (AbandonedMutexException)
        {
            // The previous owner died without releasing it; this process now owns it.
            return mutex;
        }

        mutex.Dispose();
        return null;
    }

    /// <summary>Builds the platform-configured Avalonia application.</summary>
    /// <returns>The configured application builder.</returns>
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(new FontManagerOptions
            {
                // The bundled Noto Serif SC family is exposed under an app-owned
                // alias so the embedded files always win over a system install.
                DefaultFamilyName = "HyperFidel Serif",
                FontFamilyMappings = new Dictionary<string, FontFamily>
                {
                    ["HyperFidel Serif"] =
                        new("avares://HyperFidel/Assets/Fonts#Noto Serif SC"),
                    ["HyperFidel Serif Devanagari"] =
                        new("avares://HyperFidel/Assets/Fonts#Noto Serif Devanagari")
                },
                FontFallbacks =
                [
                    new FontFallback { FontFamily = new FontFamily("HyperFidel Serif Devanagari") },
                    new FontFallback { FontFamily = new FontFamily("Noto Serif") }
                ]
            });

        if (OperatingSystem.IsMacOS())
        {
            builder = builder.With(new AvaloniaNativePlatformOptions
            {
                RenderingMode =
                [
                    AvaloniaNativeRenderingMode.OpenGl,
                    AvaloniaNativeRenderingMode.Software
                ]
            });
        }

        return builder.LogToTrace();
    }
}
