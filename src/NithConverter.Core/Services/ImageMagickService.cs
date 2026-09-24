using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

public sealed class ImageMagickService
{
    public Task<ProcessExecutionResult> ConvertAsync(string executable, string inputPath,
        string outputPath, string formatId, string scratchDirectory, string? ghostscriptPath,
        CancellationToken cancellationToken, ConversionOptions? options = null)
    {
        options ??= new();
        int threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        var arguments = new List<string>
        {
            "-limit", "thread", threads.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-limit", "memory", "256MiB", "-limit", "map", "512MiB", "-limit", "disk", "2GiB"
        };
        if (FormatCatalog.IsPagedDocument(inputPath))
            arguments.AddRange(["-density", options.DocumentDpi.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        // One output file per job: animated inputs and documents use frame/page zero.
        arguments.AddRange([inputPath + "[0]", "-auto-orient"]);
        if (formatId is "JPG" or "PDF")
            arguments.AddRange(["-background", "white", "-alpha", "remove", "-alpha", "off"]);
        if (options.MaxWidth is int width)
            arguments.AddRange(["-resize", $"{width}x>"]);
        if (formatId is "JPG" or "WEBP")
            arguments.AddRange(["-quality", options.ImageQuality.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (formatId == "PNG")
            arguments.AddRange(["-define", $"png:compression-level={options.PngCompression}"]);
        if (formatId == "GIF")
            arguments.AddRange(["-colors", options.GifColors.ToString(System.Globalization.CultureInfo.InvariantCulture)]);
        if (formatId == "ICO")
            arguments.AddRange(["-resize", "256x256>", "-define", "icon:auto-resize=256,128,64,48,32,16"]);
        arguments.Add(outputPath);
        string magickHome = Path.GetDirectoryName(executable)!;
        string inheritedPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var pathParts = new List<string> { magickHome };
        var environment = new Dictionary<string, string>
        {
            ["MAGICK_HOME"] = magickHome,
            ["MAGICK_CONFIGURE_PATH"] = magickHome,
            ["MAGICK_TEMPORARY_PATH"] = scratchDirectory,
            ["MAGICK_THREAD_LIMIT"] = threads.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (ghostscriptPath is not null)
        {
            string gsBin = Path.GetDirectoryName(ghostscriptPath)!;
            string? gsRoot = Directory.GetParent(gsBin)?.FullName;
            environment["MAGICK_GHOSTSCRIPT_PATH"] = gsBin;
            pathParts.Add(gsBin);
            if (gsRoot is not null)
            {
                string[] gsLibCandidates =
                [
                    Path.Combine(gsRoot, "lib"),
                    Path.Combine(gsRoot, "Resource", "Init"),
                    Path.Combine(gsRoot, "Resource", "Font")
                ];
                string[] existing = gsLibCandidates.Where(Directory.Exists).ToArray();
                if (existing.Length > 0) environment["GS_LIB"] = string.Join(Path.PathSeparator, existing);
            }
        }
        if (!string.IsNullOrWhiteSpace(inheritedPath)) pathParts.Add(inheritedPath);
        environment["PATH"] = string.Join(Path.PathSeparator, pathParts);
        return ProcessHelper.RunAsync(executable, arguments, cancellationToken, environment: environment);
    }
}
