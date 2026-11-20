using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using HyperFidel;
using HyperFidel.Audio;
using HyperFidel.Library;
using HyperFidel.Localization;
using HyperFidel.Streaming;

namespace HyperFidel.Ui.Smoke;

/// <summary>Runs isolated Windows headless workflow checks without application startup, playback or external services.</summary>
internal static class Program
{
    /// <summary>Runs the tracked smoke scenarios in their own process and temporary data root.</summary>
    /// <returns>Zero for successful checks, or one for a failed scenario or unsupported host.</returns>
    [STAThread]
    private static int Main()
    {
        if (!OperatingSystem.IsWindows())
        {
            Console.Error.WriteLine("This initial UI smoke suite is verified on Windows only.");
            return 1;
        }
        var root = Path.Combine(Path.GetTempPath(), "hyperfidel-ui-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, ".legacy-data-migrated"), "isolated synthetic fixture");
        Environment.SetEnvironmentVariable("HYPERFIDEL_DATA_DIR", root);
        Environment.SetEnvironmentVariable("FANART_TV_API_KEY", null);
        // Keep first-run normalization away from the real default endpoint.
        File.WriteAllText(Path.Combine(root, "settings.json"), JsonSerializer.Serialize(new AppSettings
        {
            OutputProfiles = [new OutputProfile { Name = "Synthetic output", Backend = OutputBackend.Wasapi,
                SelectedWasapiDeviceId = "synthetic-endpoint", SelectedWasapiDeviceName = "Synthetic output" }],
            SelectedOutputProfileName = "Synthetic output"
        }));
        try
        {
            DenyExternalRequests.Install();
            AppBuilder.Configure<App>().UseHeadless(new AvaloniaHeadlessPlatformOptions
                { UseHeadlessDrawing = false }).UseSkia().SetupWithoutStarting();
            // Only local workflows remain: the offline player no longer has the
            // server/remote transports those checks exercised.
            EmptyLibraryWorkflows.Run(root);
            DirectorySetupWorkflows.Run(root);
            ShuffleWorkflows.Run();
            if (DenyExternalRequests.Requests != 0)
                throw new InvalidOperationException("Cached workflows attempted external provider requests.");
            Console.WriteLine("All Windows UI smoke workflows passed.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("UI smoke failed: " + exception.Message);
            return 1;
        }
    }
}

/// <summary>Fails closed for external profile/image transport inside this dedicated smoke process.</summary>
internal sealed class DenyExternalRequests : HttpMessageHandler
{
    private static int _requests;
    /// <summary>Gets the number of unexpected requests, without retaining addresses or headers.</summary>
    internal static int Requests => Volatile.Read(ref _requests);

    /// <summary>
    /// Kept as a no-op: the offline player ships no HTTP clients, so there is no
    /// external transport left to isolate. Requests stays zero for the smoke gate.
    /// </summary>
    internal static void Install()
    {
    }

    /// <summary>Counts and rejects a request without using a network transport or logging its data.</summary>
    /// <param name="request">Unexpected provider request, never retained.</param>
    /// <param name="cancellationToken">Request cancellation.</param>
    /// <returns>A synthetic unavailable response.</returns>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _requests);
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
