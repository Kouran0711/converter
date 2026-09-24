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

    // Formatos comuns aceitos pelas distribuições atuais do ImageMagick. Alguns dependem
    // dos delegates presentes na instalação local (HEIC/AVIF/SVG/PSD etc.).
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

    public static IReadOnlyList<string> SupportedExtensions { get; } =
        Array.AsReadOnly(Images.Concat(Documents).Concat(Videos).Order().ToArray());

    public static bool IsSupportedInput(string path) => GetInputKind(path) is not null;

    public static MediaKind? GetInputKind(string path)
    {
        string extension = Path.GetExtension(path);
        if (Images.Contains(extension)) return MediaKind.Image;
        if (Videos.Contains(extension)) return MediaKind.Video;
        return Documents.Contains(extension) ? MediaKind.Document : null;
    }

    public static bool IsPagedDocument(string path) => Documents.Contains(Path.GetExtension(path));

    public static IReadOnlyList<OutputFormat> GetOutputFormats(string inputPath) => GetInputKind(inputPath) switch
    {
        MediaKind.Image or MediaKind.Document => ImageOutputs,
        MediaKind.Video => VideoOutputs,
        _ => Array.Empty<OutputFormat>()
    };

    public static OutputFormat? FindOutputFormat(string id) => ImageOutputs.Concat(VideoOutputs)
        .FirstOrDefault(format => format.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}
