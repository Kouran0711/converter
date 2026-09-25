using System.Diagnostics;
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
/// O manifesto Latest e a página de release são fallbacks quando a API pública do GitHub está indisponível.
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
    private readonly HttpClient _downloadHttp;

    public Version? LastKnownLatestVersion { get; private set; }
    public string LastCheckSource { get; private set; } = "";

    public GitHubUpdateService(string owner, string repository, HttpClient? httpClient = null)
    {
        _owner = owner;
        _repository = repository;
        _http = httpClient ?? CreateHttpClient();
        _downloadHttp = httpClient ?? CreateDownloadHttpClient();

        if (!_http.DefaultRequestHeaders.UserAgent.Any())
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NITHConverter", "1.7.3"));
        if (!_downloadHttp.DefaultRequestHeaders.UserAgent.Any())
            _downloadHttp.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NITHConverter-Updater", "1.7.3"));
    }

    private static HttpClient CreateHttpClient() => CreateHttpClientCore(TimeSpan.FromSeconds(30));

    private static HttpClient CreateDownloadHttpClient() => CreateHttpClientCore(TimeSpan.FromMinutes(30));

    private static HttpClient CreateHttpClientCore(TimeSpan timeout)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 12,
            UseProxy = true,
            DefaultProxyCredentials = CredentialCache.DefaultCredentials
        };
        return new HttpClient(handler)
        {
            Timeout = timeout,
            // GitHub's release CDN works reliably with HTTP/1.1 even on machines/proxies where HTTP/2 is filtered.
            DefaultRequestVersion = HttpVersion.Version11,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    public async Task<UpdateRelease?> CheckAsync(Version currentVersion, CancellationToken cancellationToken = default)
    {
        Version current = Normalize(currentVersion);
        LastKnownLatestVersion = null;
        LastCheckSource = "";

        // Caminho principal: a API lista as releases estáveis e permite escolher a MAIOR versão,
        // sem depender do marcador Latest (que pode ficar atrasado ou ter sido configurado errado).
        Exception? apiFailure = null;
        try
        {
            ApiRelease? candidate = await GetHighestStableReleaseFromApiAsync(cancellationToken).ConfigureAwait(false);
            LastCheckSource = "GitHub Releases API";
            LastKnownLatestVersion = candidate?.Version;
            if (candidate is null || candidate.Version <= current) return null;
            return await BuildReleaseFromApiAsync(candidate, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsRecoverableCheckFailure(ex)) { apiFailure = ex; }

        // Fallback sem limite normal da API: manifesto pequeno da release marcada como Latest.
        Exception? manifestFailure = null;
        try
        {
            UpdateManifest? manifest = await TryReadManifestAsync(BuildLatestAssetUri(ManifestAssetName), cancellationToken).ConfigureAwait(false);
            if (manifest is not null)
            {
                LastCheckSource = "Manifesto da release Latest";
                LastKnownLatestVersion = manifest.Version;
                if (manifest.Version <= current) return null;

                return new UpdateRelease(
                    manifest.Version,
                    manifest.Tag,
                    manifest.Title,
                    manifest.InstallerFileName,
                    BuildAssetUri(manifest.Tag, manifest.InstallerFileName),
                    null,
                    manifest.Sha256,
                    manifest.Size,
                    new Uri($"https://github.com/{Escape(_owner)}/{Escape(_repository)}/releases/tag/{Escape(manifest.Tag)}"));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex) when (IsRecoverableCheckFailure(ex)) { manifestFailure = ex; }

        // Último fallback: segue a página /releases/latest e tenta os nomes conhecidos.
        try
        {
            UpdateRelease? fallback = await TryGetLatestReleaseWithoutApiAsync(cancellationToken).ConfigureAwait(false);
            LastCheckSource = "GitHub Latest (fallback)";
            LastKnownLatestVersion = fallback?.Version;
            if (fallback is not null && fallback.Version > current) return fallback;
            if (fallback is not null && fallback.Version <= current) return null;

            throw new HttpRequestException("O GitHub respondeu, mas não foi possível identificar uma release estável válida.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception fallbackFailure) when (IsRecoverableCheckFailure(fallbackFailure))
        {
            HttpStatusCode? status = (apiFailure as HttpRequestException)?.StatusCode ??
                                     (manifestFailure as HttpRequestException)?.StatusCode ??
                                     (fallbackFailure as HttpRequestException)?.StatusCode;
            string details = string.Join(" | ", new[] { apiFailure?.Message, manifestFailure?.Message, fallbackFailure.Message }
                .Where(value => !string.IsNullOrWhiteSpace(value)).Take(3));
            throw new HttpRequestException(
                $"Não foi possível confirmar a versão mais recente no GitHub. {details}",
                new AggregateException(new Exception?[] { apiFailure, manifestFailure, fallbackFailure }.OfType<Exception>()),
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
            try
            {
                await ValidateDownloadedInstallerAsync(installerPath, expectedHash, cancellationToken).ConfigureAwait(false);
                progress?.Report(100);
                return new DownloadedUpdate(release, installerPath, IntegrityVerified: expectedHash is not null);
            }
            catch (InvalidDataException) { TryDelete(installerPath); }
        }

        foreach (string old in Directory.EnumerateFiles(updatesDirectory, "*.exe*"))
        {
            if (old.Equals(temporaryPath, StringComparison.OrdinalIgnoreCase)) continue;
            TryDelete(old);
        }

        var candidates = new List<Uri> { release.InstallerUri };
        Uri canonical = BuildAssetUri(release.Tag, release.InstallerFileName);
        Uri latest = BuildLatestAssetUri(release.InstallerFileName);
        if (!candidates.Any(uri => uri.AbsoluteUri.Equals(canonical.AbsoluteUri, StringComparison.OrdinalIgnoreCase))) candidates.Add(canonical);
        if (!candidates.Any(uri => uri.AbsoluteUri.Equals(latest.AbsoluteUri, StringComparison.OrdinalIgnoreCase))) candidates.Add(latest);

        var failures = new List<string>();
        foreach (Uri candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(temporaryPath);
            try
            {
                await DownloadWithHttpClientAsync(candidate, temporaryPath, release.InstallerSize, progress, cancellationToken).ConfigureAwait(false);
                await ValidateDownloadedInstallerAsync(temporaryPath, expectedHash, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, installerPath, overwrite: true);
                progress?.Report(100);
                return new DownloadedUpdate(release, installerPath, IntegrityVerified: expectedHash is not null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
            {
                failures.Add($"HTTP {candidate.Host}: {ex.Message}");
                TryDelete(temporaryPath);
            }
        }

        // BITS é o mecanismo nativo do Windows para transferências grandes e tolera melhor
        // conexões instáveis/proxies corporativos. Se o serviço estiver desativado, seguimos para curl.
        foreach (Uri candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(temporaryPath);
            try
            {
                progress?.Report(5);
                await DownloadWithBitsAsync(candidate, temporaryPath, cancellationToken).ConfigureAwait(false);
                progress?.Report(95);
                await ValidateDownloadedInstallerAsync(temporaryPath, expectedHash, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, installerPath, overwrite: true);
                progress?.Report(100);
                return new DownloadedUpdate(release, installerPath, IntegrityVerified: expectedHash is not null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or System.ComponentModel.Win32Exception)
            {
                failures.Add($"BITS {candidate.Host}: {ex.Message}");
                TryDelete(temporaryPath);
            }
        }

        // Windows 10/11 traz curl.exe. Ele oferece uma terceira rota quando HttpClient/BITS
        // são bloqueados ou alterados por proxy, antivírus ou filtro de rede.
        foreach (Uri candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TryDelete(temporaryPath);
            try
            {
                progress?.Report(5);
                await DownloadWithCurlAsync(candidate, temporaryPath, cancellationToken).ConfigureAwait(false);
                progress?.Report(95);
                await ValidateDownloadedInstallerAsync(temporaryPath, expectedHash, cancellationToken).ConfigureAwait(false);
                File.Move(temporaryPath, installerPath, overwrite: true);
                progress?.Report(100);
                return new DownloadedUpdate(release, installerPath, IntegrityVerified: expectedHash is not null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException or System.ComponentModel.Win32Exception)
            {
                failures.Add($"curl {candidate.Host}: {ex.Message}");
                TryDelete(temporaryPath);
            }
        }

        string detail = failures.Count == 0 ? "nenhuma rota de download ficou disponível" : string.Join(" | ", failures.Take(6));
        throw new HttpRequestException($"Não foi possível baixar o instalador da atualização após tentar as rotas do GitHub. {detail}");
    }

    private async Task DownloadWithHttpClientAsync(Uri uri, string destination, long expectedSize,
        IProgress<double>? progress, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        using HttpResponseMessage response = await _downloadHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        if (response.StatusCode == HttpStatusCode.NotFound)
            throw new HttpRequestException("O instalador não existe nessa release.", null, response.StatusCode);
        response.EnsureSuccessStatusCode();

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("O GitHub retornou uma página HTML no lugar do instalador.");

        long? total = response.Content.Headers.ContentLength ?? (expectedSize > 0 ? expectedSize : null);
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        await using FileStream output = new(destination, FileMode.Create, FileAccess.Write, FileShare.None,
            256 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[256 * 1024];
        long received = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            received += read;
            if (total is > 0) progress?.Report(Math.Clamp(received * 100d / total.Value, 0, 99));
        }
        await output.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task DownloadWithBitsAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        string windowsPowerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");
        string executable = File.Exists(windowsPowerShell) ? windowsPowerShell : "powershell.exe";
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.Environment["NITH_UPDATE_URL"] = uri.AbsoluteUri;
        startInfo.Environment["NITH_UPDATE_DEST"] = destination;
        foreach (string argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command",
            "$ErrorActionPreference='Stop'; Import-Module BitsTransfer -ErrorAction Stop; " +
            "Start-BitsTransfer -Source $env:NITH_UPDATE_URL -Destination $env:NITH_UPDATE_DEST " +
            "-TransferType Download -Priority Foreground -ErrorAction Stop"
        }) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new IOException("Não foi possível iniciar o BITS para baixar a atualização.");
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        string stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new HttpRequestException($"BITS retornou código {process.ExitCode}: {stderr.Trim()}");
    }

    private static async Task DownloadWithCurlAsync(Uri uri, string destination, CancellationToken cancellationToken)
    {
        string systemCurl = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
        string executable = File.Exists(systemCurl) ? systemCurl : "curl.exe";
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (string argument in new[]
        {
            "--fail", "--location", "--silent", "--show-error",
            "--retry", "4", "--retry-delay", "2", "--retry-all-errors",
            "--connect-timeout", "20", "--max-time", "1800",
            "--user-agent", "NITHConverter-Updater/1.7.3",
            "--output", destination, uri.AbsoluteUri
        }) startInfo.ArgumentList.Add(argument);

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new IOException("Não foi possível iniciar o fallback de download do Windows.");
        Task<string> stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        string stderr = await stderrTask.ConfigureAwait(false);
        _ = await stdoutTask.ConfigureAwait(false);
        if (process.ExitCode != 0)
            throw new HttpRequestException($"curl retornou código {process.ExitCode}: {stderr.Trim()}");
    }

    private static async Task ValidateDownloadedInstallerAsync(string path, string? expectedHash, CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length < 64 * 1024)
            throw new InvalidDataException("O arquivo baixado é pequeno demais para ser o instalador do NITH Converter.");
        await using (FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                         4096, FileOptions.Asynchronous | FileOptions.SequentialScan))
        {
            byte[] header = new byte[2];
            int count = await stream.ReadAsync(header, cancellationToken).ConfigureAwait(false);
            if (count != 2 || header[0] != (byte)'M' || header[1] != (byte)'Z')
                throw new InvalidDataException("O download concluído não é um executável Windows válido.");
        }
        if (expectedHash is not null)
        {
            string actualHash = await ComputeSha256Async(path, cancellationToken).ConfigureAwait(false);
            if (!actualHash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A verificação SHA-256 da atualização falhou. O instalador foi descartado.");
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

    private Uri BuildLatestAssetUri(string assetName) => new(
        $"https://github.com/{Escape(_owner)}/{Escape(_repository)}/releases/latest/download/{Escape(assetName)}");

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
