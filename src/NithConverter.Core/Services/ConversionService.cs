using System.Diagnostics;
using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

public sealed class ConversionService(DependencyService dependencies, HistoryService history, LocalLogger logger)
{
    private readonly ImageMagickService _images = new();
    private readonly FFmpegService _videos = new();
    private readonly DocumentService _documents = new();
    private readonly OfficeDocumentService _officeDocuments = new();
    private readonly SemaphoreSlim _singleJob = new(1, 1);

    public string GetOutputPath(ConversionRequest request)
    {
        string input = Path.GetFullPath(request.InputPath);
        OutputFormat format = FormatCatalog.FindOutputFormat(request.OutputFormatId)
            ?? throw new ArgumentException("Formato de saída não suportado.");
        string directory = string.IsNullOrWhiteSpace(request.DestinationDirectory)
            ? Path.GetDirectoryName(input)! : Path.GetFullPath(request.DestinationDirectory);
        if (OutputNameRules.GetError(request.OutputBaseName) is { } nameError)
            throw new ArgumentException(nameError, nameof(request.OutputBaseName));
        string baseName = request.OutputBaseName ?? Path.GetFileNameWithoutExtension(input);
        string output = Path.Combine(directory, baseName + format.Extension);
        if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
            output = Path.Combine(directory, baseName + "-convertido" + format.Extension);
        return output;
    }

    /// <summary>Offloads filesystem metadata and process startup once, at the service boundary.</summary>
    public Task<ConversionResult> ConvertAsync(ConversionRequest request,
        IProgress<ConversionProgress>? progress = null, CancellationToken cancellationToken = default) =>
        Task.Run(() => ConvertCoreAsync(request, progress, cancellationToken), CancellationToken.None);

    private async Task<ConversionResult> ConvertCoreAsync(ConversionRequest request,
        IProgress<ConversionProgress>? progress, CancellationToken token)
    {
        var elapsed = Stopwatch.StartNew();
        string? output = null, scratch = null, externalScratch = null;
        bool ownsGate = false;
        try
        {
            await _singleJob.WaitAsync(token).ConfigureAwait(false);
            ownsGate = true;
            var options = request.Options ?? new ConversionOptions();
            if (options.GetError() is { } optionsError)
                return Failure(ConversionError.InvalidInput, optionsError);
            string input = Path.GetFullPath(request.InputPath);
            if (!File.Exists(input)) return Failure(ConversionError.InvalidInput, "O arquivo selecionado não está mais disponível.");
            MediaKind? kind = FormatCatalog.GetInputKind(input);
            OutputFormat? format = FormatCatalog.GetOutputFormats(input).FirstOrDefault(
                candidate => candidate.Id.Equals(request.OutputFormatId, StringComparison.OrdinalIgnoreCase));
            if (kind is null || format is null)
                return Failure(ConversionError.UnsupportedFormat, "Escolha um formato compatível com o arquivo selecionado.");
            if (OutputNameRules.GetError(request.OutputBaseName) is { } nameError)
                return Failure(ConversionError.InvalidInput, nameError);
            output = GetOutputPath(request);
            if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase))
                return Failure(ConversionError.InvalidInput, "Escolha um destino diferente do arquivo original.");
            if (File.Exists(output) && !request.Overwrite)
                return Failure(ConversionError.OutputExists, "Esse arquivo já existe. Confirme a substituição ou escolha outro destino.");

            DependencySnapshot installed = await dependencies.DiscoverAsync(cancellationToken: token).ConfigureAwait(false);
            bool officeDocument = kind == MediaKind.Document && FormatCatalog.IsOfficeDocument(input);
            bool directOfficePdf = officeDocument && format.Id.Equals("PDF", StringComparison.OrdinalIgnoreCase);
            if ((kind is MediaKind.Video or MediaKind.Audio) && installed.FFmpegPath is null)
                return Failure(ConversionError.DependencyMissing, "O FFmpeg interno não foi encontrado. Reinstale o NITH Converter para restaurar os componentes de conversão.");
            if (officeDocument && installed.LibreOfficePath is null)
                return Failure(ConversionError.DependencyMissing,
                    "O mecanismo de documentos Office não foi encontrado. Reinstale o NITH Converter para instalar o LibreOffice automaticamente.");
            if ((kind == MediaKind.Image || (kind == MediaKind.Document && !directOfficePdf)) && installed.ImageMagickPath is null)
                return Failure(ConversionError.DependencyMissing, "O ImageMagick interno não foi encontrado. Reinstale o NITH Converter para restaurar os componentes de conversão.");
            if (kind == MediaKind.Document && !directOfficePdf && installed.GhostscriptPath is null)
                return Failure(ConversionError.DependencyMissing,
                    "O componente interno de documentos não foi encontrado. Reinstale o NITH Converter para restaurar o Ghostscript.");

            // Keep the source read-only and prevent writers/deletion while the child uses its path.
            await using var sourceGuard = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read,
                64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            string directory = Path.GetDirectoryName(output)!;
            Directory.CreateDirectory(directory);
            var originalDestination = DestinationStamp.Read(output);
            if (originalDestination is not null && !request.Overwrite)
                return Failure(ConversionError.OutputExists, "Esse arquivo já existe. Confirme a substituição.");
            scratch = Path.Combine(directory, ".nith-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(scratch);
            string stagedOutput = Path.Combine(scratch, "result" + format.Extension);
            string engineInput = input, engineOutput = stagedOutput, engineScratch = scratch;
            // Some Windows builds of the CLI tools still use MAX_PATH APIs. Only those jobs
            // need a short working directory; .NET streams handle long paths at the edges.
            if (input.Length >= 240 || stagedOutput.Length >= 240)
            {
                externalScratch = Path.Combine(Path.GetTempPath(), "NITHConverter", Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(externalScratch);
                engineScratch = externalScratch;
                engineOutput = Path.Combine(externalScratch, "result" + format.Extension);
                if (input.Length >= 240)
                {
                    FFmpegService.Report(progress, new(null, "Preparando arquivo com caminho longo…"));
                    engineInput = Path.Combine(externalScratch, "input" + Path.GetExtension(input));
                    await using var inputCopy = new FileStream(engineInput, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 64 * 1024, FileOptions.Asynchronous);
                    await sourceGuard.CopyToAsync(inputCopy, 64 * 1024, token).ConfigureAwait(false);
                    await inputCopy.FlushAsync(token).ConfigureAwait(false);
                }
            }
            token.ThrowIfCancellationRequested();
            await logger.WriteAsync("conversion.started", $"{kind}:{format.Id}").ConfigureAwait(false);
            bool directDocumentOutputReady = false;
            if (kind == MediaKind.Document)
            {
                if (officeDocument)
                {
                    FFmpegService.Report(progress, new(null, "Convertendo documento Office para PDF…"));
                    string officePdf = directOfficePdf ? engineOutput : Path.Combine(engineScratch, "office-document.pdf");
                    ProcessExecutionResult officeResult = await _officeDocuments.ConvertToPdfAsync(
                        installed.LibreOfficePath!, engineInput, officePdf, engineScratch, token).ConfigureAwait(false);
                    if (officeResult.ExitCode != 0 || !File.Exists(officePdf) || new FileInfo(officePdf).Length == 0)
                    {
                        await logger.WriteAsync("office.convert_failed", officeResult.StandardError).ConfigureAwait(false);
                        return Failure(ConversionError.Failed,
                            "Não foi possível abrir o documento do Word, Excel ou PowerPoint. Verifique se o arquivo está íntegro ou protegido por senha.",
                            string.Join(Environment.NewLine, new[] { officeResult.StandardOutput, officeResult.StandardError }.Where(x => !string.IsNullOrWhiteSpace(x))));
                    }
                    if (directOfficePdf)
                    {
                        directDocumentOutputReady = true;
                    }
                    else
                    {
                        engineInput = officePdf;
                    }
                }

                if (!directDocumentOutputReady)
                {
                    FFmpegService.Report(progress, new(null, "Preparando a primeira página do documento…"));
                    string renderedPage = Path.Combine(engineScratch, "document-page-1.png");
                    ProcessExecutionResult documentRender = await _documents.RasterizeFirstPageAsync(
                        engineInput, renderedPage, options.DocumentDpi, token).ConfigureAwait(false);
                    if (documentRender.ExitCode != 0 || !File.Exists(renderedPage) || new FileInfo(renderedPage).Length == 0)
                    {
                        await logger.WriteAsync("document.render_failed", documentRender.StandardError).ConfigureAwait(false);
                        return Failure(ConversionError.Failed,
                            "Não foi possível renderizar o documento. Verifique se ele está íntegro e não está protegido por senha.",
                            documentRender.StandardError);
                    }
                    engineInput = renderedPage;
                }
            }

            FFmpegService.Report(progress, new(null, directDocumentOutputReady ? "Finalizando PDF…" : "Convertendo…"));
            bool useFfmpeg = kind is MediaKind.Video or MediaKind.Audio;
            ProcessExecutionResult execution = directDocumentOutputReady
                ? new ProcessExecutionResult(0, string.Empty, string.Empty)
                : useFfmpeg
                    ? await _videos.ConvertAsync(installed.FFmpegPath!, installed.FFprobePath, engineInput,
                        engineOutput, format.Id, progress, token, options).ConfigureAwait(false)
                    : await _images.ConvertAsync(installed.ImageMagickPath!, engineInput, engineOutput,
                        format.Id, engineScratch, null, token, options).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (execution.ExitCode != 0 || !File.Exists(engineOutput) || new FileInfo(engineOutput).Length == 0)
            {
                await logger.WriteAsync("conversion.failed", $"exit:{execution.ExitCode}").ConfigureAwait(false);
                string friendly = execution.StandardError.Contains("security policy", StringComparison.OrdinalIgnoreCase)
                    ? "A política de segurança do ImageMagick bloqueou este formato. Consulte os detalhes e a configuração da dependência."
                    : "Não foi possível converter o arquivo. Verifique se ele está íntegro e se o formato escolhido é compatível.";
                return Failure(ConversionError.Failed, friendly, $"Código {execution.ExitCode}\n{execution.StandardError}");
            }
            FFmpegService.Report(progress, new(null, "Finalizando arquivo…"));
            if (externalScratch is not null)
            {
                await using var completed = new FileStream(engineOutput, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var destination = new FileStream(stagedOutput, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 64 * 1024, FileOptions.Asynchronous);
                await completed.CopyToAsync(destination, 64 * 1024, token).ConfigureAwait(false);
                await destination.FlushAsync(token).ConfigureAwait(false);
            }
            // A new/changed destination requires a new confirmation, even after a long conversion.
            if (originalDestination != DestinationStamp.Read(output))
                return Failure(ConversionError.OutputExists, "O arquivo de destino mudou durante a conversão. Confirme novamente a substituição.");
            token.ThrowIfCancellationRequested();
            try
            {
                // Same-volume rename commits only the complete file. No-overwrite is enforced by OS.
                File.Move(stagedOutput, output, request.Overwrite && originalDestination is not null);
            }
            catch (IOException) when (File.Exists(output) && (!request.Overwrite || originalDestination is null))
            { return Failure(ConversionError.OutputExists, "Esse arquivo já existe. Confirme a substituição."); }

            // Commit is the cancellation boundary: once complete, a late cancellation cannot undo it.
            string message = "Conversão concluída.";
            try
            {
                await history.AddAsync(new(DateTimeOffset.UtcNow, Path.GetFileName(input), Path.GetFileName(output),
                    output, format.Id, elapsed.ElapsedMilliseconds), CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                message = "Conversão concluída. Não foi possível salvar o histórico.";
                await logger.WriteAsync("history.save_failed", ex.GetType().Name).ConfigureAwait(false);
            }
            FFmpegService.Report(progress, new(100, message));
            await logger.WriteAsync("conversion.completed", $"{format.Id}:{elapsed.ElapsedMilliseconds}ms").ConfigureAwait(false);
            return new(true, false, output, message, null, elapsed.Elapsed, ConversionError.None);
        }
        catch (OperationCanceledException)
        {
            await logger.WriteAsync("conversion.canceled").ConfigureAwait(false);
            return new(false, true, null, "Conversão cancelada.", null, elapsed.Elapsed, ConversionError.Canceled);
        }
        catch (UnauthorizedAccessException ex)
        { return Failure(ConversionError.Failed, "Sem permissão para ler o arquivo ou gravar na pasta escolhida. Escolha outra pasta.", ex.Message); }
        catch (Exception ex) when (ex is IOException or ArgumentException or NotSupportedException or System.ComponentModel.Win32Exception or System.Security.SecurityException)
        {
            await logger.WriteAsync("conversion.error", ex.GetType().Name).ConfigureAwait(false);
            return Failure(ConversionError.Failed, "Não foi possível concluir a conversão. Verifique o arquivo, as dependências e o espaço disponível no destino.", ex.Message);
        }
        finally
        {
            foreach (string? ownedDirectory in new[] { scratch, externalScratch })
            {
                if (ownedDirectory is null) continue;
                try { Directory.Delete(ownedDirectory, recursive: true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { await logger.WriteAsync("conversion.cleanup_failed", ex.GetType().Name).ConfigureAwait(false); }
            }
            if (ownsGate) _singleJob.Release();
        }

        ConversionResult Failure(ConversionError error, string message, string? details = null) =>
            new(false, false, output, message, details, elapsed.Elapsed, error);
    }

    private sealed record DestinationStamp(long Length, DateTime CreationTimeUtc, DateTime LastWriteTimeUtc)
    {
        public static DestinationStamp? Read(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? new(file.Length, file.CreationTimeUtc, file.LastWriteTimeUtc) : null;
        }
    }
}
