namespace NithConverter.Core.Models;

public enum MediaKind { Image, Document, Video }
public enum ConversionError { None, Canceled, OutputExists, DependencyMissing, UnsupportedFormat, InvalidInput, Failed }

public sealed record OutputFormat(string Id, string DisplayName, string Extension)
{
    public override string ToString() => DisplayName;
}

public sealed record ConversionRequest(string InputPath, string OutputFormatId,
    string? DestinationDirectory = null, bool Overwrite = false, string? OutputBaseName = null,
    ConversionOptions? Options = null);

public sealed record ConversionProgress(double? Percent, string Message);

public sealed record ConversionResult(bool Success, bool Canceled, string? OutputPath,
    string UserMessage, string? TechnicalDetails, TimeSpan Duration, ConversionError Error);

public sealed record DependencySnapshot(string? ImageMagickPath, string? FFmpegPath,
    string? FFprobePath, string? GhostscriptPath)
{
    public bool ImagesAvailable => ImageMagickPath is not null;
    public bool VideosAvailable => FFmpegPath is not null;
    public bool PdfReadingAvailable => ImageMagickPath is not null && GhostscriptPath is not null;
}

public sealed class AppSettings
{
    public string? DefaultOutputDirectory { get; set; }
    public bool OpenFolderAfterConversion { get; set; }
    // Kept visible as a safety preference; conversions always require explicit overwrite approval.
    public bool ConfirmOverwrite { get; set; } = true;
    public bool CheckForUpdatesOnStartup { get; set; } = true;
    public bool AutoDownloadUpdates { get; set; } = true;
    public ConversionOptions DefaultConversionOptions { get; set; } = new();
}

public sealed record HistoryEntry(DateTimeOffset TimestampUtc, string InputName,
    string OutputName, string OutputPath, string Format, long DurationMs);
