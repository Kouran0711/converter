using Ghostscript.NET.Processor;
using NithConverter.Core.Helpers;

namespace NithConverter.Core.Services;

/// <summary>
/// Renders the first page of PDF/PS/EPS in-process through Ghostscript.NativeAssets.
/// This intentionally avoids the Ghostscript Windows installer, whose recent builds
/// are not reliable for unattended installation.
/// </summary>
public sealed class DocumentService
{
    public Task<ProcessExecutionResult> RasterizeFirstPageAsync(string inputPath, string outputPngPath,
        int dpi, CancellationToken cancellationToken)
    {
        return Task.Run(() => RasterizeFirstPage(inputPath, outputPngPath, dpi, cancellationToken), CancellationToken.None);
    }

    private static ProcessExecutionResult RasterizeFirstPage(string inputPath, string outputPngPath,
        int dpi, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath)!);
        try
        {
            using var processor = new GhostscriptProcessor();
            using var registration = cancellationToken.Register(() =>
            {
                try { processor.StopProcessing(); }
                catch { /* cancellation is best-effort inside the native engine */ }
            });

            var arguments = new List<string>
            {
                "-dBATCH",
                "-dNOPAUSE",
                "-dSAFER",
                "-dFirstPage=1",
                "-dLastPage=1",
                "-dTextAlphaBits=4",
                "-dGraphicsAlphaBits=4",
                "-sDEVICE=pngalpha",
                $"-r{Math.Clamp(dpi, 72, 600)}",
                $"-sOutputFile={outputPngPath}"
            };
            if (Path.GetExtension(inputPath).Equals(".eps", StringComparison.OrdinalIgnoreCase))
                arguments.Add("-dEPSCrop");
            arguments.Add(inputPath);

            processor.Process(arguments.ToArray());
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(outputPngPath) || new FileInfo(outputPngPath).Length == 0)
                return new(1, string.Empty, "Ghostscript terminou sem produzir a primeira página do documento.");
            return new(0, string.Empty, string.Empty);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new(1, string.Empty, ex.ToString());
        }
    }
}
