namespace NithConverter.Core.Models;

public enum VideoQuality { Compact, Balanced, High }

// Immutable per-job snapshot: editing preferences never changes a running encoder.
public sealed record ConversionOptions
{
    public int ImageQuality { get; init; } = 90;
    public int PngCompression { get; init; } = 6;
    public VideoQuality VideoQuality { get; init; } = VideoQuality.Balanced;
    public int? MaxWidth { get; init; }
    public int? FramesPerSecond { get; init; }
    public int AudioBitrateKbps { get; init; } = 160;
    public int AudioSampleRateHz { get; init; } = 48000;
    public int AudioChannels { get; init; } = 2;
    public int DocumentDpi { get; init; } = 150;
    public int GifColors { get; init; } = 256;
    public bool KeepAudio { get; init; } = true;

    public string? GetError()
    {
        if (ImageQuality is < 1 or > 100) return "A qualidade da imagem deve estar entre 1 e 100.";
        if (PngCompression is < 0 or > 9) return "A compressão PNG deve estar entre 0 e 9.";
        if (!Enum.IsDefined(VideoQuality)) return "Escolha uma qualidade de vídeo válida.";
        if (MaxWidth is < 16 or > 7680) return "A largura deve estar entre 16 e 7680 pixels.";
        if (FramesPerSecond is < 1 or > 120) return "Escolha uma taxa entre 1 e 120 FPS.";
        if (AudioBitrateKbps is < 64 or > 320) return "A qualidade do áudio deve ficar entre 64 e 320 kbps.";
        if (AudioSampleRateHz is not (22050 or 32000 or 44100 or 48000 or 96000)) return "Escolha uma taxa de amostragem de áudio válida.";
        if (AudioChannels is not (1 or 2)) return "Escolha áudio mono ou estéreo.";
        if (DocumentDpi is < 72 or > 600) return "A resolução de documentos deve ficar entre 72 e 600 DPI.";
        if (GifColors is not (64 or 128 or 256)) return "Escolha 64, 128 ou 256 cores para o GIF.";
        return null;
    }
}
