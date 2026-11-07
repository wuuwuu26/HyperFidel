using HyperFidel.Streaming;
using Xunit;

namespace HyperFidel.Core.Tests;

/// <summary>Verifies atomic publication of downloaded HyperFidel Server backups.</summary>
public sealed class HyperFidelServerBackupDownloadTests
{
    /// <summary>Ensures the temporary output stream is closed before the file is renamed on Windows.</summary>
    [Fact]
    public async Task WriteDownloadAtomicallyAsync_ClosesTemporaryFileBeforeRename()
    {
        var root = Path.Combine(Path.GetTempPath(), $"hyperfidel-backup-download-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var destinationPath = Path.Combine(root, "backup.zip");
        var expected = new byte[] { 1, 2, 3, 4, 5 };

        try
        {
            await using var input = new MemoryStream(expected);
            await HyperFidelServerClient.WriteDownloadAtomicallyAsync(
                input,
                destinationPath,
                expected.Length,
                progress: null,
                CancellationToken.None);

            Assert.Equal(expected, await File.ReadAllBytesAsync(destinationPath));
            Assert.False(File.Exists(destinationPath + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
