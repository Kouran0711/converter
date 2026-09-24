using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

/// <summary>
/// Consulta releases públicas do GitHub e baixa somente o instalador acompanhado de SHA-256.
/// O serviço não instala nada sozinho: a elevação/UAC fica a cargo do aplicativo Windows.
/// </summary>
public sealed class GitHubUpdateService
{
    private readonly string _owner;
    private readonly string _repository;
    private readonly HttpClient _http;

    public GitHubUpdateService(string owner, string repository, HttpClient? httpClient = null)
    {
        _owner = owner;
        _repository = repository;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NITHConverter", "1.0"));
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        if (!_http.DefaultRequestHeaders.Contains("X-GitHub-Api-Version"))
            _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<UpdateRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await _http.GetAsync(
            $"https://api.github.com/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/latest",
            HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null; // Repositório ainda sem release publicada.

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement root = json.RootElement;

        string tag = root.GetProperty("tag_name").GetString() ?? string.Empty;
        if (!TryParseVersion(tag, out Version? remoteVersion) || remoteVersion is null || remoteVersion <= Normalize(currentVersion))
            return null;

        string title = root.TryGetProperty("name", out JsonElement nameElement) && !string.IsNullOrWhiteSpace(nameElement.GetString())
            ? nameElement.GetString()!
            : $"NITH Converter {tag}";
        string releasePage = root.GetProperty("html_url").GetString() ?? "https://github.com/Kouran0711/converter/releases";

        JsonElement.ArrayEnumerator assets = root.GetProperty("assets").EnumerateArray();
        var assetList = assets.Select(asset => new ReleaseAsset(
            asset.GetProperty("name").GetString() ?? string.Empty,
            asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
            asset.TryGetProperty("size", out JsonElement size) ? size.GetInt64() : 0)).ToArray();

        ReleaseAsset? installer = assetList.FirstOrDefault(asset =>
            asset.Name.StartsWith("NITHConverter-Setup-x64-", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        if (installer is null || string.IsNullOrWhiteSpace(installer.Url))
            throw new InvalidDataException("A release mais recente não contém o instalador x64 esperado.");

        ReleaseAsset? checksum = assetList.FirstOrDefault(asset =>
            asset.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (checksum is null || string.IsNullOrWhiteSpace(checksum.Url))
            throw new InvalidDataException("A release mais recente não contém o SHA-256 do instalador.");

        return new UpdateRelease(remoteVersion, tag, title, installer.Name, new Uri(installer.Url),
            new Uri(checksum.Url), installer.Size, new Uri(releasePage));
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string updatesDirectory = Path.Combine(StoragePaths.DataDirectory, "Updates");
        Directory.CreateDirectory(updatesDirectory);
        string installerPath = Path.Combine(updatesDirectory, release.InstallerFileName);
        string temporaryPath = installerPath + ".download";
        string expectedHash = await ReadExpectedHashAsync(release.ChecksumUri, cancellationToken).ConfigureAwait(false);

        if (File.Exists(installerPath))
        {
            string cachedHash = await ComputeSha256Async(installerPath, cancellationToken).ConfigureAwait(false);
            if (cachedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(100);
                return new DownloadedUpdate(release, installerPath);
            }
            try { File.Delete(installerPath); } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }

        foreach (string old in Directory.EnumerateFiles(updatesDirectory, "NITHConverter-Setup-x64-*.exe*"))
        {
            if (old.Equals(temporaryPath, StringComparison.OrdinalIgnoreCase)) continue;
            try { File.Delete(old); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        try
        {
            using HttpResponseMessage response = await _http.GetAsync(release.InstallerUri,
                HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            long? total = response.Content.Headers.ContentLength ?? (release.InstallerSize > 0 ? release.InstallerSize : null);
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using FileStream output = new(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None,
                128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] buffer = new byte[128 * 1024];
            long received = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                received += read;
                if (total is > 0) progress?.Report(Math.Clamp(received * 100d / total.Value, 0, 100));
            }
            await output.FlushAsync(cancellationToken).ConfigureAwait(false);

            string actualHash = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A verificação SHA-256 da atualização falhou.");

            File.Move(temporaryPath, installerPath, overwrite: true);
            progress?.Report(100);
            return new DownloadedUpdate(release, installerPath);
        }
        catch
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch (Exception) { }
            throw;
        }
    }

    private async Task<string> ReadExpectedHashAsync(Uri checksumUri, CancellationToken token)
    {
        string text = await _http.GetStringAsync(checksumUri, token).ConfigureAwait(false);
        string tokenValue = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (tokenValue.Length != 64 || tokenValue.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidDataException("O arquivo SHA-256 publicado na release é inválido.");
        return tokenValue;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken token)
    {
        await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            128 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, token).ConfigureAwait(false);
        return Convert.ToHexString(hash);
    }

    private static bool TryParseVersion(string value, out Version? version)
    {
        string clean = value.Trim().TrimStart('v', 'V');
        int suffix = clean.IndexOfAny(['-', '+']);
        if (suffix >= 0) clean = clean[..suffix];
        if (!Version.TryParse(clean, out Version? parsed)) { version = null; return false; }
        version = Normalize(parsed);
        return true;
    }

    private static Version Normalize(Version version) => new(
        Math.Max(0, version.Major), Math.Max(0, version.Minor),
        Math.Max(0, version.Build), Math.Max(0, version.Revision));

    private sealed record ReleaseAsset(string Name, string Url, long Size);
}
