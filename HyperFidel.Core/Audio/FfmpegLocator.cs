using System.IO;
using System.Runtime.InteropServices;

namespace HyperFidel.Audio;

/// <summary>
/// Locates <c>ffmpeg</c> and <c>ffprobe</c> binaries on the local system. HyperFidel is an
/// offline player and never downloads tools; the binaries must be installed through the
/// operating system or be present in the application or per-user cache directory. After a
/// successful locate the directory that contains the binaries is prepended to the current
/// process <c>PATH</c> so all <see cref="System.Diagnostics.ProcessStartInfo"/> callers can
/// reference them by bare name without modification.
/// </summary>
public static class FfmpegLocator
{
    private static string FfmpegBinary =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";

    private static string FfprobeBinary =>
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffprobe.exe" : "ffprobe";

    /// <summary>
    /// Ensures that <c>ffmpeg</c> and <c>ffprobe</c> are reachable as child processes using only
    /// local installation directories and the current process <c>PATH</c>. No network request is
    /// ever made.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the binaries are available locally; otherwise
    /// <see langword="false"/>.
    /// </returns>
    public static Task<bool> EnsureAvailableAsync()
    {
        EnsureValidCurrentDirectory();
        var installedDirectory = FindToolsDirectory();
        if (installedDirectory is null)
            return Task.FromResult(false);

        PrependToPath(installedDirectory);
        return Task.FromResult(true);
    }

    /// <summary>
    /// Determines whether both FFmpeg command-line tools are installed in a location known to HyperFidel.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when <c>ffmpeg</c> and <c>ffprobe</c> exist in the application
    /// directory, the per-user cache, a known platform installation directory, or a directory
    /// on the current process PATH.
    /// </returns>
    public static bool IsAvailable()
    {
        return FindToolsDirectory() is not null;
    }

    /// <summary>
    /// Returns an existing directory that can safely be used as
    /// <see cref="System.Diagnostics.ProcessStartInfo.WorkingDirectory"/> for FFmpeg child processes.
    /// </summary>
    /// <returns>The application base directory when it exists; otherwise the HyperFidel data directory or temp directory.</returns>
    public static string GetSafeWorkingDirectory()
    {
        if (Directory.Exists(AppContext.BaseDirectory))
        {
            return AppContext.BaseDirectory;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.DataRoot);
            return AppPaths.DataRoot;
        }
        catch
        {
            return Path.GetTempPath();
        }
    }

    /// <summary>
    /// Resets the process current directory when a shortcut or installer left it pointing to a missing path.
    /// </summary>
    public static void EnsureValidCurrentDirectory()
    {
        try
        {
            if (Directory.Exists(Environment.CurrentDirectory))
            {
                return;
            }
        }
        catch
        {
            // Fall through to reset below.
        }

        try
        {
            Environment.CurrentDirectory = GetSafeWorkingDirectory();
        }
        catch
        {
            // Child process start calls still receive an explicit safe working directory.
        }
    }

    private static string? FindToolsDirectory()
    {
        foreach (var directory in GetSearchDirectories())
        {
            try
            {
                if (File.Exists(Path.Combine(directory, FfmpegBinary)) &&
                    File.Exists(Path.Combine(directory, FfprobeBinary)))
                {
                    return directory;
                }
            }
            catch
            {
                // Ignore inaccessible or malformed PATH entries.
            }
        }

        return null;
    }

    private static IEnumerable<string> GetSearchDirectories()
    {
        yield return AppContext.BaseDirectory;
        yield return AppPaths.GetDataPath("ffmpeg");

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            yield return directory;
        }

        if (!OperatingSystem.IsMacOS())
            yield break;

        // Finder-launched .app bundles receive a minimal PATH that normally omits package-manager
        // prefixes, so probe the conventional Apple Silicon, Intel, MacPorts, pkgsrc, and Fink paths.
        string[] macDirectories =
        [
            "/opt/homebrew/bin",
            "/opt/homebrew/opt/ffmpeg/bin",
            "/usr/local/bin",
            "/usr/local/opt/ffmpeg/bin",
            "/opt/local/bin",
            "/opt/pkg/bin",
            "/sw/bin"
        ];
        foreach (var directory in macDirectories)
            yield return directory;

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
            yield return Path.Combine(userProfile, ".local", "bin");
    }

    private static void PrependToPath(string directory)
    {
        var current = Environment.GetEnvironmentVariable("PATH") ?? "";
        if (current.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                   .Any(p => string.Equals(p, directory, StringComparison.OrdinalIgnoreCase)))
            return;
        Environment.SetEnvironmentVariable("PATH", directory + Path.PathSeparator + current);
    }
}
