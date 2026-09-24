using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

/// <summary>
/// Atualização pública via GitHub Releases.
/// Primeiro usa um manifesto estável publicado como asset da release, evitando limites da API.
/// Se o manifesto ainda não existir (releases antigas), usa a API REST como fallback.
/// </summary>
public sealed class GitHubUpdateService
{
    private const string ManifestAssetName = "nith-update.json";
    private readonly string _owner;
    private readonly string _repository;
    private readonly HttpClient _http;

    public GitHubUpdateService(string owner, string repository, HttpClient? httpClient = null)
    {
        _owner = owner;
        _repository = repository;
        _http = httpClient ?? CreateHttpClient();

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NITHConverter", "1.0"));
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 8
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(25),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    public async Task<UpdateRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        Exception? manifestFailure = null;
        try
        {
            (bool found, UpdateRelease? release) = await TryCheckManifestAsync(currentVersion, cancellationToken).ConfigureAwait(false);
            if (found) return release;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException)
        {
            // Releases antigas podem não ter o manifesto; a API REST continua sendo um fallback compatível.
            manifestFailure = ex;
        }

        try
        {
            return await CheckApiAsync(currentVersion, cancellationToken).ConfigureAwait(false);
        }
        catch (HttpRequestException apiFailure) when (manifestFailure is not null)
        {
            throw new HttpRequestException(
                "O GitHub não respondeu à consulta de atualização pelo manifesto nem pela API.",
                new AggregateException(manifestFailure, apiFailure),
                apiFailure.StatusCode);
        }
    }

    private async Task<(bool Found, UpdateRelease? Release)> TryCheckManifestAsync(
        Version currentVersion, CancellationToken cancellationToken)
    {
        Uri manifestUri = new($"https://github.com/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/latest/download/{ManifestAssetName}");
        using HttpResponseMessage response = await _http.GetAsync(manifestUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return (false, null);

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement root = json.RootElement;

        string versionText = RequiredString(root, "version");
        string tag = RequiredString(root, "tag");
        string installerName = RequiredString(root, "installer");
        string expectedHash = RequiredString(root, "sha256").Trim();
        string title = OptionalString(root, "title") ?? $"NITH Converter {versionText}";

        if (!TryParseVersion(versionText, out Version? remoteVersion) || remoteVersion is null)
            throw new InvalidDataException("O manifesto de atualização contém uma versão inválida.");
        if (!IsSha256(expectedHash))
            throw new InvalidDataException("O manifesto de atualização contém um SHA-256 inválido.");

        if (remoteVersion <= Normalize(currentVersion))
            return (true, null);

        string tagSegment = Uri.EscapeDataString(tag);
        string assetSegment = Uri.EscapeDataString(installerName);
        var installerUri = new Uri($"https://github.com/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/download/{tagSegment}/{assetSegment}");
        var releasePage = new Uri($"https://github.com/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/tag/{tagSegment}");
        long size = root.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long parsedSize) ? parsedSize : 0;

        return (true, new UpdateRelease(remoteVersion, tag, title, installerName, installerUri,
            null, expectedHash.ToUpperInvariant(), size, releasePage));
    }

    private async Task<UpdateRelease?> CheckApiAsync(Version currentVersion, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Uri.EscapeDataString(_owner)}/{Uri.EscapeDataString(_repository)}/releases/latest");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", "2022-11-28");
        using HttpResponseMessage response = await _http.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;

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
        string releasePage = root.GetProperty("html_url").GetString() ?? $"https://github.com/{_owner}/{_repository}/releases";

        var assetList = root.GetProperty("assets").EnumerateArray().Select(asset => new ReleaseAsset(
            asset.GetProperty("name").GetString() ?? string.Empty,
            asset.GetProperty("browser_download_url").GetString() ?? string.Empty,
            asset.TryGetProperty("size", out JsonElement size) ? size.GetInt64() : 0)).ToArray();

        ReleaseAsset? installer = assetList.FirstOrDefault(asset =>
            asset.Name.Equals("NITH Converter.exe", StringComparison.OrdinalIgnoreCase));
        installer ??= assetList.FirstOrDefault(asset =>
            asset.Name.StartsWith("NITHConverter-Setup-x64-", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        installer ??= assetList.FirstOrDefault(asset =>
            asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains("NITH", StringComparison.OrdinalIgnoreCase));

        if (installer is null || string.IsNullOrWhiteSpace(installer.Url))
            throw new InvalidDataException("A release mais recente não contém o instalador do NITH Converter.");

        ReleaseAsset? checksum = assetList.FirstOrDefault(asset =>
            asset.Name.Equals(installer.Name + ".sha256", StringComparison.OrdinalIgnoreCase));
        if (checksum is null || string.IsNullOrWhiteSpace(checksum.Url))
            throw new InvalidDataException("A release mais recente não contém o SHA-256 do instalador.");

        return new UpdateRelease(remoteVersion, tag, title, installer.Name, new Uri(installer.Url),
            new Uri(checksum.Url), null, installer.Size, new Uri(releasePage));
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string updatesDirectory = Path.Combine(StoragePaths.DataDirectory, "Updates");
        Directory.CreateDirectory(updatesDirectory);
        string installerPath = Path.Combine(updatesDirectory, release.InstallerFileName);
        string temporaryPath = installerPath + ".download";

        string expectedHash = release.ExpectedSha256 ??
            await ReadExpectedHashAsync(release.ChecksumUri ?? throw new InvalidDataException("SHA-256 da atualização não informado."), cancellationToken).ConfigureAwait(false);

        if (File.Exists(installerPath))
        {
            string cachedHash = await ComputeSha256Async(installerPath, cancellationToken).ConfigureAwait(false);
            if (cachedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                progress?.Report(100);
                return new DownloadedUpdate(release, installerPath);
            }
            TryDelete(installerPath);
        }

        foreach (string old in Directory.EnumerateFiles(updatesDirectory, "*.exe*"))
        {
            if (old.Equals(temporaryPath, StringComparison.OrdinalIgnoreCase)) continue;
            TryDelete(old);
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
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task<string> ReadExpectedHashAsync(Uri checksumUri, CancellationToken token)
    {
        string text = await _http.GetStringAsync(checksumUri, token).ConfigureAwait(false);
        string tokenValue = text.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        if (!IsSha256(tokenValue))
            throw new InvalidDataException("O arquivo SHA-256 publicado na release é inválido.");
        return tokenValue.ToUpperInvariant();
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

    private static string RequiredString(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement element) || element.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(element.GetString()))
            throw new InvalidDataException($"Campo obrigatório ausente no manifesto: {name}.");
        return element.GetString()!;
    }

    private static string? OptionalString(JsonElement root, string name) =>
        root.TryGetProperty(name, out JsonElement element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool IsSha256(string value) =>
        value.Length == 64 && value.All(Uri.IsHexDigit);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record ReleaseAsset(string Name, string Url, long Size);
}
