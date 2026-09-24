namespace NithConverter.Core.Models;

/// <summary>One catalog controls both the picker and conversion capability validation.</summary>
public static class FormatCatalog
{
    private static readonly IReadOnlyList<OutputFormat> ImageOutputs = Array.AsReadOnly<OutputFormat>(
    [
        new("PNG", "PNG", ".png"), new("JPG", "JPG", ".jpg"),
        new("WEBP", "WEBP", ".webp"), new("BMP", "BMP", ".bmp"),
        new("TIFF", "TIFF", ".tiff"), new("GIF", "GIF", ".gif"),
        new("PDF", "PDF", ".pdf"), new("ICO", "ICO", ".ico"), new("TGA", "TGA", ".tga")
    ]);
    private static readonly IReadOnlyList<OutputFormat> VideoOutputs = Array.AsReadOnly<OutputFormat>(
    [new("GIF", "GIF", ".gif"), new("MP4", "MP4", ".mp4"), new("WEBM", "WEBM", ".webm")]);
    private static readonly IReadOnlyList<OutputFormat> AudioOutputs = Array.AsReadOnly<OutputFormat>(
    [
        new("MP3", "MP3", ".mp3"), new("WAV", "WAV", ".wav"), new("FLAC", "FLAC", ".flac"),
        new("AAC", "AAC", ".aac"), new("M4A", "M4A", ".m4a"), new("OGG", "OGG Vorbis", ".ogg"),
        new("OPUS", "Opus", ".opus"), new("WMA", "WMA", ".wma")
    ]);

    private static readonly HashSet<string> Images = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tiff", ".tif", ".gif", ".ico", ".tga",
        ".avif", ".heic", ".heif", ".jp2", ".j2k", ".dds", ".pcx", ".ppm", ".pgm", ".pbm",
        ".pnm", ".pam", ".psd", ".xcf", ".svg"
    };
    private static readonly HashSet<string> Documents = new(StringComparer.OrdinalIgnoreCase)
        { ".pdf", ".ps", ".eps" };
    private static readonly HashSet<string> Videos = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".avi", ".mov", ".mkv", ".webm", ".m4v", ".wmv", ".flv", ".mpeg", ".mpg",
        ".ts", ".mts", ".m2ts", ".3gp", ".ogv"
    };
    private static readonly HashSet<string> Audios = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3", ".wav", ".flac", ".aac", ".m4a", ".ogg", ".opus", ".wma", ".aiff", ".aif",
        ".ac3", ".eac3", ".mka", ".ape", ".alac", ".amr"
    };

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        Array.AsReadOnly(Images.Concat(Documents).Concat(Videos).Concat(Audios).Order().ToArray());

    public static bool IsSupportedInput(string path) => GetInputKind(path) is not null;

    public static MediaKind? GetInputKind(string path)
    {
        string extension = Path.GetExtension(path);
        if (Images.Contains(extension)) return MediaKind.Image;
        if (Videos.Contains(extension)) return MediaKind.Video;
        if (Audios.Contains(extension)) return MediaKind.Audio;
        return Documents.Contains(extension) ? MediaKind.Document : null;
    }

    public static bool IsPagedDocument(string path) => Documents.Contains(Path.GetExtension(path));
    public static bool IsAudioOutput(string? id) => id is not null && AudioOutputs.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
    public static bool IsVideoOutput(string? id) => id is not null && VideoOutputs.Any(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    public static IReadOnlyList<OutputFormat> GetOutputFormats(string inputPath) => GetInputKind(inputPath) switch
    {
        MediaKind.Image or MediaKind.Document => ImageOutputs,
        // Vídeos também podem ter a faixa de áudio extraída diretamente.
        MediaKind.Video => Array.AsReadOnly(VideoOutputs.Concat(AudioOutputs).ToArray()),
        MediaKind.Audio => AudioOutputs,
        _ => Array.Empty<OutputFormat>()
    };

    public static OutputFormat? FindOutputFormat(string id) => ImageOutputs.Concat(VideoOutputs).Concat(AudioOutputs)
        .FirstOrDefault(format => format.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
