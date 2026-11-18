// Offline stand-in: preserves public signatures but performs no network I/O.
namespace HyperFidel.Streaming;


public sealed class PlexServerClient : System.IDisposable
{
        public Task<IReadOnlyList<PlexLibrarySection>> GetAudioLibrariesAsync(
        PlexServerSettings server,
        string? token,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<IReadOnlyList<PlexLibrarySection>>(default!);
    }

        public Task<PlexMediaPage> GetLibraryItemsAsync(
        PlexServerSettings server,
        string? token,
        string sectionKey,
        int mediaType,
        int start,
        int size,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PlexMediaPage>(default!);
    }

        public Task<PlexMediaPage> GetChildrenAsync(
        PlexServerSettings server,
        string? token,
        string ratingKey,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PlexMediaPage>(default!);
    }

        public Task<PlexMediaPage> GetFoldersAsync(
        PlexServerSettings server,
        string? token,
        string sectionKey,
        string? parent,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult<PlexMediaPage>(default!);
    }

        public static string CreateStreamUrl(
        PlexServerSettings server,
        string partKey,
        string? token)
    {
        return string.Empty;
    }

        public static Uri NormalizeBaseUri(string value)
    {
        return new Uri("about:blank");
    }

    public void Dispose() { }
}
