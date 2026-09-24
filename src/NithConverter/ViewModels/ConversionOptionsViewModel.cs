using Microsoft.UI.Xaml;
using NithConverter.Core.Models;
using NithConverter.Helpers;

namespace NithConverter.ViewModels;

public sealed record OptionChoice<T>(T Value, string Label);

public sealed class ConversionOptionsViewModel : ObservableObject
{
    public IReadOnlyList<OptionChoice<int>> WidthChoices { get; } = new OptionChoice<int>[]
    {
        new(0, "Original / automática"), new(640, "640 px"), new(720, "720 px"), new(960, "960 px"),
        new(1280, "1280 px · HD"), new(1920, "1920 px · Full HD"), new(2560, "2560 px · QHD"),
        new(3840, "3840 px · 4K"), new(7680, "7680 px · 8K")
    };
    public IReadOnlyList<OptionChoice<int>> FpsChoices { get; } = new OptionChoice<int>[]
    {
        new(0, "Original / automático"), new(8, "8 FPS"), new(12, "12 FPS"), new(15, "15 FPS"),
        new(24, "24 FPS"), new(25, "25 FPS"), new(30, "30 FPS"), new(50, "50 FPS"), new(60, "60 FPS"), new(120, "120 FPS")
    };
    public IReadOnlyList<OptionChoice<int>> CompressionChoices { get; } = new OptionChoice<int>[]
    { new(0, "Rápida"), new(6, "Equilibrada"), new(9, "Máxima") };
    public IReadOnlyList<OptionChoice<int>> ColorChoices { get; } = new OptionChoice<int>[]
    { new(64, "64 cores · menor arquivo"), new(128, "128 cores"), new(256, "256 cores · melhor qualidade") };
    public IReadOnlyList<OptionChoice<VideoQuality>> QualityChoices { get; } = new OptionChoice<VideoQuality>[]
    {
        new(VideoQuality.Compact, "Compacta · arquivo menor"),
        new(VideoQuality.Balanced, "Equilibrada"),
        new(VideoQuality.High, "Alta · melhor qualidade")
    };
    public IReadOnlyList<OptionChoice<int>> AudioBitrateChoices { get; } = new OptionChoice<int>[]
    {
        new(96, "96 kbps · econômico"), new(128, "128 kbps"), new(160, "160 kbps · equilibrado"),
        new(192, "192 kbps"), new(256, "256 kbps"), new(320, "320 kbps · máximo")
    };
    public IReadOnlyList<OptionChoice<int>> AudioSampleRateChoices { get; } = new OptionChoice<int>[]
    {
        new(22050, "22.05 kHz"), new(32000, "32 kHz"), new(44100, "44.1 kHz · música"),
        new(48000, "48 kHz · vídeo/padrão"), new(96000, "96 kHz · alta resolução")
    };
    public IReadOnlyList<OptionChoice<int>> AudioChannelChoices { get; } = new OptionChoice<int>[]
    { new(1, "Mono"), new(2, "Estéreo") };
    public IReadOnlyList<OptionChoice<int>> DocumentDpiChoices { get; } = new OptionChoice<int>[]
    {
        new(96, "96 DPI · tela"), new(150, "150 DPI · equilibrado"), new(300, "300 DPI · impressão"), new(600, "600 DPI · alta definição")
    };

    private OptionChoice<int> _width;
    private OptionChoice<int> _fps;
    private OptionChoice<int> _compression;
    private OptionChoice<int> _colors;
    private OptionChoice<int> _audioBitrate;
    private OptionChoice<int> _audioSampleRate;
    private OptionChoice<int> _audioChannels;
    private OptionChoice<int> _documentDpi;
    private OptionChoice<VideoQuality> _videoQuality;
    private double _imageQuality = 90;
    private bool _keepAudio = true;
    private MediaKind? _kind;
    private string? _format;
    private bool _edited;

    public ConversionOptionsViewModel()
    {
        _width = WidthChoices[0]; _fps = FpsChoices[0]; _compression = CompressionChoices[1];
        _colors = ColorChoices[2]; _videoQuality = QualityChoices[1];
        _audioBitrate = AudioBitrateChoices[2]; _audioSampleRate = AudioSampleRateChoices[3];
        _audioChannels = AudioChannelChoices[1]; _documentDpi = DocumentDpiChoices[1];
    }
    public OptionChoice<int> SelectedWidth { get => _width; set { if (value is not null && Set(ref _width, value)) Changed(); } }
    public OptionChoice<int> SelectedFps { get => _fps; set { if (value is not null && Set(ref _fps, value)) Changed(); } }
    public OptionChoice<int> SelectedCompression { get => _compression; set { if (value is not null && Set(ref _compression, value)) Changed(); } }
    public OptionChoice<int> SelectedColors { get => _colors; set { if (value is not null && Set(ref _colors, value)) Changed(); } }
    public OptionChoice<int> SelectedAudioBitrate { get => _audioBitrate; set { if (value is not null && Set(ref _audioBitrate, value)) Changed(); } }
    public OptionChoice<int> SelectedAudioSampleRate { get => _audioSampleRate; set { if (value is not null && Set(ref _audioSampleRate, value)) Changed(); } }
    public OptionChoice<int> SelectedAudioChannels { get => _audioChannels; set { if (value is not null && Set(ref _audioChannels, value)) Changed(); } }
    public OptionChoice<int> SelectedDocumentDpi { get => _documentDpi; set { if (value is not null && Set(ref _documentDpi, value)) Changed(); } }
    public OptionChoice<VideoQuality> SelectedVideoQuality { get => _videoQuality; set { if (value is not null && Set(ref _videoQuality, value)) Changed(); } }
    public double ImageQuality { get => _imageQuality; set { if (double.IsFinite(value) && Set(ref _imageQuality, Math.Clamp(Math.Round(value), 1, 100))) { Raise(nameof(ImageQualityLabel)); Changed(); } } }
    public string ImageQualityLabel => $"{ImageQuality:0} / 100";
    public bool KeepAudio { get => _keepAudio; set { if (Set(ref _keepAudio, value)) Changed(); } }

    private bool IsAudioOutput => FormatCatalog.IsAudioOutput(_format);
    private bool IsVideoOutput => FormatCatalog.IsVideoOutput(_format);
    public Visibility AvailableVisibility => _kind is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility VisualResizeVisibility => ((_kind is MediaKind.Image or MediaKind.Document) || (_kind == MediaKind.Video && !IsAudioOutput)) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ImageQualityVisibility => ((_kind is MediaKind.Image or MediaKind.Document) && (_format is "JPG" or "WEBP")) ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PngVisibility => _format == "PNG" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VideoQualityVisibility => _kind == MediaKind.Video && IsVideoOutput && _format != "GIF" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility VideoVisibility => _kind == MediaKind.Video && IsVideoOutput ? Visibility.Visible : Visibility.Collapsed;
    public Visibility KeepAudioVisibility => _kind == MediaKind.Video && IsVideoOutput && _format != "GIF" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AudioOptionsVisibility => _kind == MediaKind.Audio || IsAudioOutput || KeepAudioVisibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility AudioBitrateVisibility => AudioOptionsVisibility == Visibility.Visible && _format is not "WAV" and not "FLAC" ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DocumentVisibility => _kind == MediaKind.Document ? Visibility.Visible : Visibility.Collapsed;
    public Visibility GifVisibility => _format == "GIF" ? Visibility.Visible : Visibility.Collapsed;
    public string ContextLabel => _kind switch
    {
        MediaKind.Video => "VÍDEO",
        MediaKind.Audio => "ÁUDIO",
        MediaKind.Document => "DOCUMENTO",
        MediaKind.Image => "IMAGEM",
        _ => "IMAGENS · PDF · VÍDEOS · ÁUDIO"
    };
    public string Hint => _kind == MediaKind.Audio || IsAudioOutput
        ? _format is "WAV" or "FLAC"
            ? "Conversão de áudio sem perda/PCM. Escolha taxa de amostragem e mono/estéreo; bitrate não se aplica a este formato."
            : "Escolha bitrate, taxa de amostragem e canais. Bitrate maior tende a preservar mais qualidade e gerar arquivo maior."
        : _kind == MediaKind.Video
            ? _format == "GIF"
                ? $"GIF sem áudio · {(_fps.Value == 0 ? 12 : _fps.Value)} FPS · até {(_width.Value == 0 ? 960 : _width.Value)} px. Mais FPS e cores aumentam o arquivo."
                : "FPS e resolução em automático preservam a origem. Qualidade alta e bitrate de áudio maior aumentam o tamanho final."
            : _kind == MediaKind.Document
                ? $"A primeira página é renderizada em {_documentDpi.Value} DPI. DPI maior melhora detalhes e também aumenta uso de memória e tempo."
                : _format == "PNG"
                    ? "PNG usa compressão sem perda: máxima pode reduzir o tamanho, mas demora mais. A largura automática preserva a resolução."
                    : "A largura automática preserva a resolução. Redimensionar mantém a proporção, sem ampliar. Qualidade maior pode gerar arquivos maiores.";

    public void Configure(MediaKind? kind, string? format)
    {
        _kind = kind; _format = format;
        foreach (var name in new[]
        {
            nameof(AvailableVisibility), nameof(VisualResizeVisibility), nameof(ImageQualityVisibility), nameof(PngVisibility),
            nameof(VideoQualityVisibility), nameof(VideoVisibility), nameof(KeepAudioVisibility), nameof(AudioOptionsVisibility),
            nameof(AudioBitrateVisibility), nameof(DocumentVisibility), nameof(GifVisibility), nameof(ContextLabel), nameof(Hint)
        }) Raise(name);
    }
    public ConversionOptions Snapshot() => new()
    {
        ImageQuality = (int)ImageQuality, MaxWidth = _width.Value == 0 ? null : _width.Value,
        FramesPerSecond = _fps.Value == 0 ? null : _fps.Value, PngCompression = _compression.Value,
        VideoQuality = _videoQuality.Value, GifColors = _colors.Value, KeepAudio = KeepAudio,
        AudioBitrateKbps = _audioBitrate.Value, AudioSampleRateHz = _audioSampleRate.Value,
        AudioChannels = _audioChannels.Value, DocumentDpi = _documentDpi.Value
    };
    public void Apply(ConversionOptions options, bool onlyIfUntouched = false)
    {
        if (onlyIfUntouched && _edited) return;
        if (options.GetError() is not null) options = new();
        ImageQuality = options.ImageQuality;
        SelectedWidth = WidthChoices.FirstOrDefault(x => x.Value == (options.MaxWidth ?? 0)) ?? WidthChoices[0];
        SelectedFps = FpsChoices.FirstOrDefault(x => x.Value == (options.FramesPerSecond ?? 0)) ?? FpsChoices[0];
        SelectedCompression = CompressionChoices.FirstOrDefault(x => x.Value == options.PngCompression) ?? CompressionChoices[1];
        SelectedColors = ColorChoices.FirstOrDefault(x => x.Value == options.GifColors) ?? ColorChoices[2];
        SelectedVideoQuality = QualityChoices.FirstOrDefault(x => x.Value == options.VideoQuality) ?? QualityChoices[1];
        SelectedAudioBitrate = AudioBitrateChoices.FirstOrDefault(x => x.Value == options.AudioBitrateKbps) ?? AudioBitrateChoices[2];
        SelectedAudioSampleRate = AudioSampleRateChoices.FirstOrDefault(x => x.Value == options.AudioSampleRateHz) ?? AudioSampleRateChoices[3];
        SelectedAudioChannels = AudioChannelChoices.FirstOrDefault(x => x.Value == options.AudioChannels) ?? AudioChannelChoices[1];
        SelectedDocumentDpi = DocumentDpiChoices.FirstOrDefault(x => x.Value == options.DocumentDpi) ?? DocumentDpiChoices[1];
        KeepAudio = options.KeepAudio;
    }
    private void Changed() { _edited = true; Raise(nameof(Hint)); }
}
