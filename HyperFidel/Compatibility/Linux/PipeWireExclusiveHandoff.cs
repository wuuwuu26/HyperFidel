using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace HyperFidel.Audio;

/// <summary>
/// Temporarily removes a PipeWire/PulseAudio card profile so HyperFidel can open the
/// same ALSA hardware exclusively, then restores the previous profile on release.
/// </summary>
internal sealed partial class PipeWireExclusiveHandoff : IDisposable
{
    private readonly string _cardName;
    private readonly string _previousProfile;
    private bool _restored;

    private PipeWireExclusiveHandoff(string cardName, string previousProfile)
    {
        _cardName = cardName;
        _previousProfile = previousProfile;
    }

    /// <summary>Tries to release a matching PipeWire/PulseAudio card profile.</summary>
    /// <param name="alsaDeviceId">Direct ALSA identifier such as <c>alsa:hw:CARD=Audio,DEV=0</c>.</param>
    /// <returns>A restore handle, or <see langword="null"/> when no handoff is needed or possible.</returns>
    public static PipeWireExclusiveHandoff? TrySuspend(string alsaDeviceId)
    {
        if (!OperatingSystem.IsLinux())
            return null;

        var cardId = ExtractCardId(alsaDeviceId);
        if (cardId is null)
            return null;

        var card = FindCard(cardId);
        if (card is not { } target ||
            string.IsNullOrWhiteSpace(target.Profile) ||
            string.Equals(target.Profile, "off", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var marker = Path.Combine(AppPaths.DataRoot, "pipewire-handoff.json");
        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            File.WriteAllText(
                marker,
                JsonSerializer.Serialize(new { card = target.Name, profile = target.Profile }));
        }
        catch
        {
            // The handoff still works; the marker only protects a crashed process.
        }

        if (!RunPactl("set-card-profile", target.Name, "off"))
        {
            TryDelete(marker);
            return null;
        }

        var handoff = new PipeWireExclusiveHandoff(target.Name, target.Profile);
        AppDomain.CurrentDomain.ProcessExit += handoff.OnProcessExit;
        return handoff;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Restore();
        AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
    }

    private void OnProcessExit(object? sender, EventArgs e) => Restore();

    private void Restore()
    {
        if (_restored)
            return;
        _restored = true;
        RunPactl("set-card-profile", _cardName, _previousProfile);
        TryDelete(Path.Combine(AppPaths.DataRoot, "pipewire-handoff.json"));
    }

    /// <summary>Restores a card profile left disabled by an earlier crashed process.</summary>
    public static void RestorePending()
    {
        if (!OperatingSystem.IsLinux())
            return;

        var marker = Path.Combine(AppPaths.DataRoot, "pipewire-handoff.json");
        if (!File.Exists(marker))
            return;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(marker));
            var card = document.RootElement.GetProperty("card").GetString();
            var profile = document.RootElement.GetProperty("profile").GetString();
            if (!string.IsNullOrWhiteSpace(card) && !string.IsNullOrWhiteSpace(profile))
                RunPactl("set-card-profile", card, profile);
        }
        catch
        {
        }
        TryDelete(marker);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    private static string? ExtractCardId(string deviceId)
    {
        var match = CardIdRegex().Match(deviceId);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static PipeWireCard? FindCard(string cardId)
    {
        if (!TryRunPactl(["list", "cards"], out var output))
            return null;

        string? name = null;
        string? id = null;
        string? profile = null;
        foreach (var line in output.Split('\n'))
        {
            var trimmed = line.Trim();
            if (trimmed.StartsWith("device.name = \"", StringComparison.Ordinal))
            {
                if (name is not null && string.Equals(id, cardId, StringComparison.Ordinal))
                    return new PipeWireCard(name, profile ?? string.Empty);
                name = ExtractQuoted(trimmed);
                id = null;
                profile = null;
            }
            else if (trimmed.StartsWith("alsa.id = \"", StringComparison.Ordinal))
            {
                id = ExtractQuoted(trimmed);
            }
            else if (trimmed.StartsWith("Active Profile:", StringComparison.Ordinal))
            {
                profile = trimmed["Active Profile:".Length..].Trim();
            }
        }

        return name is not null && string.Equals(id, cardId, StringComparison.Ordinal)
            ? new PipeWireCard(name, profile ?? string.Empty)
            : null;
    }

    private static string ExtractQuoted(string line)
    {
        var first = line.IndexOf('"');
        var last = line.LastIndexOf('"');
        return first >= 0 && last > first ? line[(first + 1)..last] : string.Empty;
    }

    private static bool RunPactl(params string[] arguments) =>
        TryRunPactl(arguments, out _);

    private static bool TryRunPactl(string[] arguments, out string output)
    {
        output = string.Empty;
        try
        {
            var startInfo = new ProcessStartInfo("pactl")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.Environment["LC_ALL"] = "C";
            foreach (var argument in arguments)
                startInfo.ArgumentList.Add(argument);

            using var process = Process.Start(startInfo);
            if (process is null)
                return false;
            output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(3000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return process.ExitCode == 0;
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record PipeWireCard(string Name, string Profile);

    [GeneratedRegex(@"CARD=([^,\s]+)", RegexOptions.IgnoreCase)]
    private static partial Regex CardIdRegex();
}
