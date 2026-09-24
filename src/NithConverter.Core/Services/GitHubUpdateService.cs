using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

/// <summary>
/// Atualização pública via GitHub Releases.
/// A consulta principal lista as releases estáveis e escolhe a maior versão sem depender do marcador "Latest".
/// O marcador Latest + manifesto continua como fallback quando a API pública do GitHub está indisponível.
/// </summary>
public sealed class GitHubUpdateService
{
    private const string ManifestAssetName = "nith-update.json";
    private const string GitHubApiVersion = "2026-03-10";
    private static readonly string[] KnownInstallerNames =
    [
        "NITH.Converter.exe",
        "NITH Converter.exe",
        "NITHConverter.exe"
    ];

    private readonly string _owner;
    private readonly string _repository;
    private readonly HttpClient _http;

    public Version? LastKnownLatestVersion { get; private set; }
    public string LastCheckSource { get; private set; } = "";

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
        Version current = Normalize(currentVersion);
        LastKnownLatestVersion = null;
        LastCheckSource = "";

        Exception? apiFailure = null;
        try
        {
            ApiRelease? candidate = await GetHighestStableReleaseFromApiAsync(cancellationToken).ConfigureAwait(false);
            LastCheckSource = "GitHub Releases";
            LastKnownLatestVersion = candidate?.Version;

            if (candidate is null || candidate.Version <= current)
                return null;

            return await BuildReleaseFromApiAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (IsRecoverableCheckFailure(ex))
        {
            apiFailure = ex;
        }

        // Fallback sem API: segue /releases/latest, lê a tag final e tenta manifesto/nomes conhecidos.
        // Se ele disser que não há versão nova, NÃO declaramos "atualizado" porque a consulta completa falhou.
        try
        {
            UpdateRelease? fallback = await TryGetLatestReleaseWithoutApiAsync(cancellationToken).ConfigureAwait(false);
            LastCheckSource = "GitHub Latest (fallback)";
            LastKnownLatestVersion = fallback?.Version;

            if (fallback is not null && fallback.Version > current)
                return fallback;

            string observed = fallback is null ? "nenhuma versão identificável" : FormatVersion(fallback.Version);
            throw new HttpRequestException(
                $"O GitHub informou {observed} pelo fallback, mas a lista completa de releases não pôde ser consultada. " +
                "Por segurança o aplicativo não vai afirmar que está atualizado até conseguir confirmar a lista de releases.",
                apiFailure);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception fallbackFailure) when (IsRecoverableCheckFailure(fallbackFailure))
        {
            if (fallbackFailure is HttpRequestException http && http.InnerException == apiFailure)
                throw;

            HttpStatusCode? status = (apiFailure as HttpRequestException)?.StatusCode ??
                                     (fallbackFailure as HttpRequestException)?.StatusCode;
            throw new HttpRequestException(
                "Não foi possível confirmar a versão mais recente no GitHub. A consulta pela API e o fallback público falharam.",
                new AggregateException(apiFailure ?? fallbackFailure, fallbackFailure),
                status);
        }
    }

    private async Task<ApiRelease?> GetHighestStableReleaseFromApiAsync(CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get,
            $"https://api.github.com/repos/{Escape(_owner)}/{Escape(_repository)}/releases?per_page=100");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.Add("X-GitHub-Api-Version", GitHubApiVersion);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };

        using HttpResponseMessage response = await SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException("O repositório de atualizações não foi encontrado no GitHub.", null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.TooManyRequests)
            throw CreateRateLimitException(response);

        response.EnsureSuccessStatusCode();
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (json.RootElement.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("O GitHub retornou uma lista de releases em formato inesperado.");

        var releases = new List<ApiRelease>();
        foreach (JsonElement item in json.RootElement.EnumerateArray())
        {
            bool draft = item.TryGetProperty("draft", out JsonElement draftElement) && draftElement.ValueKind == JsonValueKind.True;
            bool prerelease = item.TryGetProperty("prerelease", out JsonElement preElement) && preElement.ValueKind == JsonValueKind.True;
            if (draft || prerelease) continue;

            string tag = OptionalString(item, "tag_name") ?? "";
            if (!TryParseVersion(tag, out Version? version) || version is null) continue;

            string title = OptionalString(item, "name") ?? $"NITH Converter {FormatVersion(version)}";
            string pageText = OptionalString(item, "html_url") ??
                              $"https://github.com/{_owner}/{_repository}/releases/tag/{Escape(tag)}";
            if (!Uri.TryCreate(pageText, UriKind.Absolute, out Uri? pageUri))
                pageUri = new Uri($"https://github.com/{_owner}/{_repository}/releases/tag/{Escape(tag)}");

            var assets = new List<ReleaseAsset>();
            if (item.TryGetProperty("assets", out JsonElement assetsElement) && assetsElement.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement asset in assetsElement.EnumerateArray())
                {
                    string name = OptionalString(asset, "name") ?? "";
                    string url = OptionalString(asset, "browser_download_url") ?? "";
                    long size = asset.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long parsedSize)
                        ? parsedSize : 0;
                    string? digest = OptionalString(asset, "digest");
                    assets.Add(new ReleaseAsset(name, url, size, ParseSha256Digest(digest)));
                }
            }

            releases.Add(new ApiRelease(version, tag, title, pageUri, assets));
        }

        return releases
            .OrderByDescending(release => release.Version)
            .FirstOrDefault();
    }

    private async Task<UpdateRelease> BuildReleaseFromApiAsync(ApiRelease candidate, CancellationToken cancellationToken)
    {
        ReleaseAsset? manifestAsset = candidate.Assets.FirstOrDefault(asset =>
            asset.Name.Equals(ManifestAssetName, StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(asset.Url, UriKind.Absolute, out _));

        if (manifestAsset is not null)
        {
            try
            {
                UpdateManifest? manifest = await TryReadManifestAsync(new Uri(manifestAsset.Url), cancellationToken).ConfigureAwait(false);
                if (manifest is not null && manifest.Version == candidate.Version)
                {
                    ReleaseAsset? installerFromManifest = candidate.Assets.FirstOrDefault(asset =>
                        asset.Name.Equals(manifest.InstallerFileName, StringComparison.OrdinalIgnoreCase));
                    Uri installerUri = installerFromManifest is not null && Uri.TryCreate(installerFromManifest.Url, UriKind.Absolute, out Uri? assetUri)
                        ? assetUri
                        : BuildAssetUri(candidate.Tag, manifest.InstallerFileName);
                    long size = installerFromManifest?.Size ?? manifest.Size;
                    string? hash = manifest.Sha256 ?? installerFromManifest?.Sha256;
                    Uri? checksum = FindChecksumUri(candidate.Assets, manifest.InstallerFileName);

                    return new UpdateRelease(candidate.Version, candidate.Tag, candidate.Title,
                        manifest.InstallerFileName, installerUri, checksum, hash, size, candidate.ReleasePageUri);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException)
            {
                // O manifesto é um acelerador/conferência extra; a lista de assets da própria release ainda é autoritativa.
            }
        }

        ReleaseAsset? installer = SelectInstaller(candidate.Assets);
        if (installer is null || !Uri.TryCreate(installer.Url, UriKind.Absolute, out Uri? installerUrl))
            throw new InvalidDataException(
                $"A release {candidate.Tag} existe, mas não contém um instalador reconhecível do NITH Converter.");

        Uri? checksumUri = FindChecksumUri(candidate.Assets, installer.Name);
        return new UpdateRelease(candidate.Version, candidate.Tag, candidate.Title, installer.Name,
            installerUrl, checksumUri, installer.Sha256, installer.Size, candidate.ReleasePageUri);
    }

    private async Task<UpdateRelease?> TryGetLatestReleaseWithoutApiAsync(CancellationToken cancellationToken)
    {
        Uri latestPage = new($"https://github.com/{Escape(_owner)}/{Escape(_repository)}/releases/latest");
        using HttpResponseMessage pageResponse = await GetAsync(latestPage, cancellationToken).ConfigureAwait(false);
        if (pageResponse.StatusCode == HttpStatusCode.NotFound) return null;
        pageResponse.EnsureSuccessStatusCode();

        Uri finalUri = pageResponse.RequestMessage?.RequestUri ?? latestPage;
        string? tag = ExtractTagFromReleaseUri(finalUri);
        if (string.IsNullOrWhiteSpace(tag) || !TryParseVersion(tag, out Version? version) || version is null)
            return null;

        Uri releasePage = new($"https://github.com/{Escape(_owner)}/{Escape(_repository)}/releases/tag/{Escape(tag)}");
        Uri manifestUri = BuildAssetUri(tag, ManifestAssetName);
        try
        {
            UpdateManifest? manifest = await TryReadManifestAsync(manifestUri, cancellationToken).ConfigureAwait(false);
            if (manifest is not null && manifest.Version == version)
            {
                return new UpdateRelease(version, tag, manifest.Title, manifest.InstallerFileName,
                    BuildAssetUri(tag, manifest.InstallerFileName), null, manifest.Sha256, manifest.Size, releasePage);
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or JsonException)
        {
            // Continua procurando nomes conhecidos para releases antigas.
        }

        foreach (string installerName in KnownInstallerNames)
        {
            Uri installerUri = BuildAssetUri(tag, installerName);
            AssetProbe? probe = await ProbeAssetAsync(installerUri, cancellationToken).ConfigureAwait(false);
            if (probe is null) continue;

            Uri checksumUri = BuildAssetUri(tag, installerName + ".sha256");
            AssetProbe? checksum = await ProbeAssetAsync(checksumUri, cancellationToken).ConfigureAwait(false);
            return new UpdateRelease(version, tag, $"NITH Converter {FormatVersion(version)}", installerName,
                installerUri, checksum is null ? null : checksumUri, null, probe.Size, releasePage);
        }

        throw new InvalidDataException($"A release {tag} foi encontrada, mas o instalador não pôde ser localizado.");
    }

    public async Task<DownloadedUpdate> DownloadAsync(UpdateRelease release,
        IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        string updatesDirectory = Path.Combine(StoragePaths.DataDirectory, "Updates");
        Directory.CreateDirectory(updatesDirectory);
        string installerPath = Path.Combine(updatesDirectory, SanitizeFileName(release.InstallerFileName));
        string temporaryPath = installerPath + ".download";

        string? expectedHash = release.ExpectedSha256;
        if (expectedHash is null && release.ChecksumUri is not null)
            expectedHash = await ReadExpectedHashAsync(release.ChecksumUri, cancellationToken).ConfigureAwait(false);

        if (File.Exists(installerPath))
        {
            if (expectedHash is not null)
            {
                string cachedHash = await ComputeSha256Async(installerPath, cancellationToken).ConfigureAwait(false);
                if (cachedHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(100);
                    return new DownloadedUpdate(release, installerPath, IntegrityVerified: true);
                }
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
            using HttpResponseMessage response = await GetAsync(release.InstallerUri, cancellationToken).ConfigureAwait(false);
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

            bool verified = false;
            if (expectedHash is not null)
            {
                string actualHash = await ComputeSha256Async(temporaryPath, cancellationToken).ConfigureAwait(false);
                if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("A verificação SHA-256 da atualização falhou. O instalador foi descartado.");
                verified = true;
            }

            File.Move(temporaryPath, installerPath, overwrite: true);
            progress?.Report(100);
            return new DownloadedUpdate(release, installerPath, verified);
        }
        catch
        {
            TryDelete(temporaryPath);
            throw;
        }
    }

    private async Task<UpdateManifest?> TryReadManifestAsync(Uri manifestUri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(manifestUri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument json = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        JsonElement root = json.RootElement;

        string versionText = RequiredString(root, "version");
        string tag = RequiredString(root, "tag");
        string installer = RequiredString(root, "installer");
        string title = OptionalString(root, "title") ?? $"NITH Converter {versionText}";
        string? hash = OptionalString(root, "sha256")?.Trim();
        long size = root.TryGetProperty("size", out JsonElement sizeElement) && sizeElement.TryGetInt64(out long parsedSize)
            ? parsedSize : 0;

        if (!TryParseVersion(versionText, out Version? version) || version is null)
            throw new InvalidDataException("O manifesto de atualização contém uma versão inválida.");
        if (!TryParseVersion(tag, out Version? tagVersion) || tagVersion is null || tagVersion != version)
            throw new InvalidDataException("A tag e a versão do manifesto de atualização não correspondem.");
        if (hash is not null && !IsSha256(hash))
            throw new InvalidDataException("O manifesto de atualização contém um SHA-256 inválido.");
        if (!installer.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(installer) != installer)
            throw new InvalidDataException("O nome do instalador no manifesto é inválido.");

        return new UpdateManifest(version, tag, title, installer,
            hash?.ToUpperInvariant(), size);
    }

    private async Task<AssetProbe?> ProbeAssetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await GetAsync(uri, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) return null;
        return new AssetProbe(response.Content.Headers.ContentLength ?? 0);
    }

    private async Task<HttpResponseMessage> GetAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        return await SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        try
        {
            return await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new HttpRequestException("A consulta ao GitHub excedeu o tempo limite.", ex);
        }
    }

    private async Task<string> ReadExpectedHashAsync(Uri checksumUri, CancellationToken token)
    {
        using HttpResponseMessage response = await GetAsync(checksumUri, token).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        string text = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
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

    private static ReleaseAsset? SelectInstaller(IReadOnlyList<ReleaseAsset> assets) => assets
        .Where(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        .Where(asset => !asset.Name.Contains("unins", StringComparison.OrdinalIgnoreCase))
        .Where(asset => !string.IsNullOrWhiteSpace(asset.Url))
        .OrderBy(InstallerScore)
        .FirstOrDefault(asset => InstallerScore(asset) < 1000);

    private static int InstallerScore(ReleaseAsset asset)
    {
        for (int i = 0; i < KnownInstallerNames.Length; i++)
            if (asset.Name.Equals(KnownInstallerNames[i], StringComparison.OrdinalIgnoreCase)) return i;
        if (asset.Name.StartsWith("NITHConverter-Setup-x64-", StringComparison.OrdinalIgnoreCase)) return 10;
        if (asset.Name.Contains("NITH", StringComparison.OrdinalIgnoreCase) &&
            asset.Name.Contains("Converter", StringComparison.OrdinalIgnoreCase)) return 20;
        return 1000;
    }

    private static Uri? FindChecksumUri(IReadOnlyList<ReleaseAsset> assets, string installerName)
    {
        ReleaseAsset? checksum = assets.FirstOrDefault(asset =>
            asset.Name.Equals(installerName + ".sha256", StringComparison.OrdinalIgnoreCase) &&
            Uri.TryCreate(asset.Url, UriKind.Absolute, out _));
        return checksum is null ? null : new Uri(checksum.Url);
    }

    private Uri BuildAssetUri(string tag, string assetName) => new(
        $"https://github.com/{Escape(_owner)}/{Escape(_repository)}/releases/download/{Escape(tag)}/{Escape(assetName)}");

    private static string? ExtractTagFromReleaseUri(Uri uri)
    {
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i + 1 < segments.Length; i++)
        {
            if (segments[i].Equals("tag", StringComparison.OrdinalIgnoreCase))
                return Uri.UnescapeDataString(segments[i + 1]);
        }
        return null;
    }

    private static string? ParseSha256Digest(string? digest)
    {
        if (string.IsNullOrWhiteSpace(digest)) return null;
        const string prefix = "sha256:";
        string value = digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? digest[prefix.Length..] : digest;
        return IsSha256(value) ? value.ToUpperInvariant() : null;
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

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

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

    private static bool IsSha256(string value) => value.Length == 64 && value.All(Uri.IsHexDigit);

    private static bool IsRecoverableCheckFailure(Exception ex) =>
        ex is HttpRequestException or IOException or InvalidDataException or JsonException or TaskCanceledException;

    private static HttpRequestException CreateRateLimitException(HttpResponseMessage response)
    {
        string? remaining = response.Headers.TryGetValues("X-RateLimit-Remaining", out IEnumerable<string>? values)
            ? values.FirstOrDefault() : null;
        string message = remaining == "0"
            ? "O limite público de consultas do GitHub foi atingido temporariamente."
            : "O GitHub recusou temporariamente a consulta pública de releases.";
        return new HttpRequestException(message, null, response.StatusCode);
    }

    private static string SanitizeFileName(string value)
    {
        string name = Path.GetFileName(value);
        foreach (char invalid in Path.GetInvalidFileNameChars())
            name = name.Replace(invalid, '_');
        return string.IsNullOrWhiteSpace(name) ? "NITH.Converter.exe" : name;
    }

    private static string Escape(string value) => Uri.EscapeDataString(value);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private sealed record ReleaseAsset(string Name, string Url, long Size, string? Sha256);
    private sealed record ApiRelease(Version Version, string Tag, string Title, Uri ReleasePageUri, IReadOnlyList<ReleaseAsset> Assets);
    private sealed record UpdateManifest(Version Version, string Tag, string Title, string InstallerFileName, string? Sha256, long Size);
    private sealed record AssetProbe(long Size);
}
