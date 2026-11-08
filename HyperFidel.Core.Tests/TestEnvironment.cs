using System.Runtime.CompilerServices;

namespace HyperFidel.Core.Tests;

/// <summary>
/// Configures a process-wide isolated data root before any type that caches
/// <see cref="HyperFidel.AppPaths.DataRoot"/> is initialized. The root is a static
/// property initializer, so the environment override must be set first; doing it
/// from a per-class constructor would race with whichever test class touches it
/// first.
/// </summary>
internal static class TestEnvironment
{
    /// <summary>Sets the data-directory override for the whole test run.</summary>
    [ModuleInitializer]
    internal static void Initialize()
    {
        Environment.SetEnvironmentVariable(
            HyperFidel.AppPaths.DataDirEnvironmentVariable,
            Path.Combine(Path.GetTempPath(), $"hyperfidel-core-tests-{Guid.NewGuid():N}"));
    }
}
