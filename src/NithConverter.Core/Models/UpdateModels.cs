namespace NithConverter.Core.Models;

public sealed record UpdateRelease(
    Version Version,
    string Tag,
    string Title,
    string InstallerFileName,
    Uri InstallerUri,
    Uri? ChecksumUri,
    string? ExpectedSha256,
    long InstallerSize,
    Uri ReleasePageUri)
{
    public bool HasIntegrityVerification =>
        !string.IsNullOrWhiteSpace(ExpectedSha256) || ChecksumUri is not null;
}

public sealed record DownloadedUpdate(
    UpdateRelease Release,
    string InstallerPath,
    bool IntegrityVerified = false);
