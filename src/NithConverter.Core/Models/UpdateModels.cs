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
    Uri ReleasePageUri);

public sealed record DownloadedUpdate(UpdateRelease Release, string InstallerPath);
