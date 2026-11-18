// Dormant update data contracts (no network I/O).
namespace HyperFidel.Updates;

/// <summary>Describes one downloadable artifact in a signed HyperFidel release manifest.</summary>
/// <param name="Component">Artifact component, such as <c>desktop</c> or <c>server</c>.</param>
/// <param name="OperatingSystem">Target operating system.</param>
/// <param name="Architecture">Target architecture.</param>
/// <param name="Type">Packaging type.</param>
/// <param name="File">GitHub Release asset file name.</param>
/// <param name="Sha256">Lowercase hexadecimal SHA-256 digest.</param>
public sealed record ReleaseAssetInfo(
    string Component,
    string OperatingSystem,
    string Architecture,
    string Type,
    string File,
    string Sha256);

/// <summary>Represents the signed update manifest attached to an HyperFidel GitHub Release.</summary>
/// <param name="Version">Semantic release version without a <c>v</c> prefix.</param>
/// <param name="Tag">Git tag that produced the release.</param>
/// <param name="Assets">Supported release artifacts and their digests.</param>
public sealed record ReleaseManifest(string Version, string Tag, IReadOnlyList<ReleaseAssetInfo> Assets);

/// <summary>Contains a verified manifest together with its original signed representation.</summary>
/// <param name="Manifest">Parsed manifest.</param>
/// <param name="ManifestBytes">Exact signed JSON bytes.</param>
/// <param name="SignatureBytes">ECDSA signature bytes.</param>
public sealed record VerifiedReleaseManifest(
    ReleaseManifest Manifest,
    byte[] ManifestBytes,
    byte[] SignatureBytes);

/// <summary>Provides authenticated discovery and download of official HyperFidel release assets.</summary>
