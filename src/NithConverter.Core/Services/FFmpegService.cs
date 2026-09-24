using System.Diagnostics;
using System.Globalization;
using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

public sealed class FFmpegService
{
    public async Task<ProcessExecutionResult> ConvertAsync(string executable, string? probeExecutable,
        string inputPath, string outputPath, string formatId, IProgress<ConversionProgress>? progress,
        CancellationToken cancellationToken, ConversionOptions? options = null)
    {
        options ??= new();
        double? duration = await GetDurationAsync(probeExecutable, inputPath, cancellationToken).ConfigureAwait(false);
        string threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4).ToString(CultureInfo.InvariantCulture);

        if (FormatCatalog.IsAudioOutput(formatId))
            return await ConvertAudioAsync(executable, inputPath, outputPath, formatId, progress,
                cancellationToken, options, duration, threads).ConfigureAwait(false);

        Report(progress, new(null, "Convertendo vídeo…"));
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-nostats", "-y",
            "-threads", threads, "-i", inputPath,
            "-progress", "pipe:1", "-stats_period", "0.4",
            "-map", "0:v:0", "-threads", threads, "-filter_threads", "1", "-filter_complex_threads", "1"
        };
        var filters = new List<string>();
        int? fps = options.FramesPerSecond ?? (formatId == "GIF" ? 12 : null);
        int? width = options.MaxWidth ?? (formatId == "GIF" ? 960 : null);
        if (fps is int rate) filters.Add($"fps={rate}");
        filters.Add(width is int limit
            ? $"scale=w='trunc(min({limit},iw)/2)*2':h=-2:flags=lanczos"
            : "scale=trunc(iw/2)*2:trunc(ih/2)*2");
        switch (formatId)
        {
            case "GIF":
                arguments.AddRange(["-an", "-vf",
                    string.Join(',', filters) + $",split[a][b];[a]palettegen=stats_mode=single:max_colors={options.GifColors}[p];[b][p]paletteuse=new=1:dither=bayer",
                    "-loop", "0"]);
                break;
            case "MP4":
                string quantizer = options.VideoQuality switch { VideoQuality.Compact => "8", VideoQuality.High => "2", _ => "3" };
                arguments.AddRange(["-vf", string.Join(',', filters),
                    "-c:v", "mpeg4", "-q:v", quantizer, "-pix_fmt", "yuv420p", "-c:a", "aac",
                    "-b:a", $"{options.AudioBitrateKbps}k", "-ar", options.AudioSampleRateHz.ToString(CultureInfo.InvariantCulture),
                    "-ac", options.AudioChannels.ToString(CultureInfo.InvariantCulture), "-movflags", "+faststart"]);
                break;
            case "WEBM":
                string crf = options.VideoQuality switch { VideoQuality.Compact => "40", VideoQuality.High => "24", _ => "32" };
                arguments.AddRange(["-vf", string.Join(',', filters), "-c:v", "libvpx-vp9", "-crf", crf,
                    "-b:v", "0", "-deadline", "good", "-cpu-used", "5", "-row-mt", "1",
                    "-c:a", "libopus", "-b:a", $"{options.AudioBitrateKbps}k",
                    "-ar", options.AudioSampleRateHz.ToString(CultureInfo.InvariantCulture),
                    "-ac", options.AudioChannels.ToString(CultureInfo.InvariantCulture)]);
                break;
            default: throw new ArgumentException("Formato de vídeo não suportado.", nameof(formatId));
        }
        if (formatId != "GIF")
            arguments.AddRange(options.KeepAudio ? ["-map", "0:a:0?"] : ["-an"]);
        arguments.Add(outputPath);
        return await RunWithProgressAsync(executable, arguments, progress, cancellationToken, duration, "Convertendo vídeo…").ConfigureAwait(false);
    }

    private static async Task<ProcessExecutionResult> ConvertAudioAsync(string executable, string inputPath,
        string outputPath, string formatId, IProgress<ConversionProgress>? progress, CancellationToken cancellationToken,
        ConversionOptions options, double? duration, string threads)
    {
        Report(progress, new(null, "Convertendo áudio…"));
        var arguments = new List<string>
        {
            "-hide_banner", "-loglevel", "error", "-nostdin", "-nostats", "-y",
            "-threads", threads, "-i", inputPath,
            "-progress", "pipe:1", "-stats_period", "0.4",
            "-map", "0:a:0", "-vn",
            "-ar", options.AudioSampleRateHz.ToString(CultureInfo.InvariantCulture),
            "-ac", options.AudioChannels.ToString(CultureInfo.InvariantCulture)
        };

        switch (formatId)
        {
            case "MP3":
                arguments.AddRange(["-c:a", "libmp3lame", "-b:a", $"{options.AudioBitrateKbps}k"]);
                break;
            case "WAV":
                arguments.AddRange(["-c:a", "pcm_s16le"]);
                break;
            case "FLAC":
                arguments.AddRange(["-c:a", "flac", "-compression_level", "8"]);
                break;
            case "AAC":
                arguments.AddRange(["-c:a", "aac", "-b:a", $"{options.AudioBitrateKbps}k"]);
                break;
            case "M4A":
                arguments.AddRange(["-c:a", "aac", "-b:a", $"{options.AudioBitrateKbps}k", "-movflags", "+faststart"]);
                break;
            case "OGG":
                arguments.AddRange(["-c:a", "libvorbis", "-b:a", $"{options.AudioBitrateKbps}k"]);
                break;
            case "OPUS":
                arguments.AddRange(["-c:a", "libopus", "-b:a", $"{options.AudioBitrateKbps}k"]);
                break;
            case "WMA":
                arguments.AddRange(["-c:a", "wmav2", "-b:a", $"{options.AudioBitrateKbps}k"]);
                break;
            default:
                throw new ArgumentException("Formato de áudio não suportado.", nameof(formatId));
        }

        arguments.Add(outputPath);
        return await RunWithProgressAsync(executable, arguments, progress, cancellationToken, duration, "Convertendo áudio…").ConfigureAwait(false);
    }

    private static async Task<ProcessExecutionResult> RunWithProgressAsync(string executable, IReadOnlyList<string> arguments,
        IProgress<ConversionProgress>? progress, CancellationToken cancellationToken, double? duration, string message)
    {
        long lastUpdate = 0;
        return await ProcessHelper.RunAsync(executable, arguments, cancellationToken, line =>
        {
            if (duration is not > 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
            if (!long.TryParse(line.AsSpan("out_time_us=".Length), CultureInfo.InvariantCulture, out long microseconds)) return;
            long now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(lastUpdate, now).TotalMilliseconds < 250) return;
            lastUpdate = now;
            double percent = Math.Clamp(microseconds / 1_000_000d / duration.Value * 100d, 0d, 99.9d);
            Report(progress, new(percent, message));
        }).ConfigureAwait(false);
    }

    private static async Task<double?> GetDurationAsync(string? executable, string input, CancellationToken token)
    {
        if (executable is null) return null;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        try
        {
            ProcessExecutionResult result = await ProcessHelper.RunAsync(executable,
                ["-v", "error", "-show_entries", "format=duration", "-of", "default=noprint_wrappers=1:nokey=1", input],
                timeout.Token).ConfigureAwait(false);
            return result.ExitCode == 0 && double.TryParse(result.StandardOutput.Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out double duration) && double.IsFinite(duration) && duration > 0
                ? duration : null;
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { return null; }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception) { return null; }
    }

    internal static void Report(IProgress<ConversionProgress>? progress, ConversionProgress value)
    {
        try { progress?.Report(value); } catch (Exception) { }
    }
}
