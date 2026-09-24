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
        var environment = new Dictionary<string, string>
        {
            ["MAGICK_TEMPORARY_PATH"] = scratchDirectory,
            ["MAGICK_THREAD_LIMIT"] = threads.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };
        if (ghostscriptPath is not null) environment["MAGICK_GHOSTSCRIPT_PATH"] = Path.GetDirectoryName(ghostscriptPath)!;
        return ProcessHelper.RunAsync(executable, arguments, cancellationToken, environment: environment);
    }
}
