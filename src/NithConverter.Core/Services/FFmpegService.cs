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
        Report(progress, new(null, "Convertendo vídeo…"));
        string threads = Math.Clamp(Environment.ProcessorCount / 2, 1, 4).ToString(CultureInfo.InvariantCulture);
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
                // Single-frame palettes stream through FFmpeg: no full-video palette buffering.
                arguments.AddRange(["-an", "-vf",
                    string.Join(',', filters) + $",split[a][b];[a]palettegen=stats_mode=single:max_colors={options.GifColors}[p];[b][p]paletteuse=new=1:dither=bayer",
                    "-loop", "0"]);
                break;
            case "MP4":
                // Native MPEG-4/AAC avoid requiring a GPL-enabled libx264 distribution.
                string quantizer = options.VideoQuality switch { VideoQuality.Compact => "8", VideoQuality.High => "2", _ => "3" };
                arguments.AddRange(["-vf", string.Join(',', filters),
                    "-c:v", "mpeg4", "-q:v", quantizer, "-pix_fmt", "yuv420p", "-c:a", "aac",
                    "-b:a", $"{options.AudioBitrateKbps}k", "-movflags", "+faststart"]);
                break;
            case "WEBM":
                string crf = options.VideoQuality switch { VideoQuality.Compact => "40", VideoQuality.High => "24", _ => "32" };
                arguments.AddRange(["-vf", string.Join(',', filters), "-c:v", "libvpx-vp9", "-crf", crf,
                    "-b:v", "0", "-deadline", "good", "-cpu-used", "5", "-row-mt", "1",
                    "-c:a", "libopus", "-b:a", $"{options.AudioBitrateKbps}k"]);
                break;
            default: throw new ArgumentException("Formato de vídeo não suportado.", nameof(formatId));
        }
        if (formatId != "GIF")
            arguments.AddRange(options.KeepAudio ? ["-map", "0:a:0?"] : ["-an"]);
        arguments.Add(outputPath);
        long lastUpdate = 0;
        return await ProcessHelper.RunAsync(executable, arguments, cancellationToken, line =>
        {
            if (duration is not > 0 || !line.StartsWith("out_time_us=", StringComparison.Ordinal)) return;
            if (!long.TryParse(line.AsSpan("out_time_us=".Length), CultureInfo.InvariantCulture, out long microseconds)) return;
            long now = Stopwatch.GetTimestamp();
            if (Stopwatch.GetElapsedTime(lastUpdate, now).TotalMilliseconds < 250) return;
            lastUpdate = now;
            // Duration metadata can be imperfect. 100% is reserved for successful final commit.
            double percent = Math.Clamp(microseconds / 1_000_000d / duration.Value * 100d, 0d, 99.9d);
            Report(progress, new(percent, "Convertendo vídeo…"));
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
        // A UI observer cannot interrupt pipe draining or strand the external encoder.
        try { progress?.Report(value); } catch (Exception) { }
    }
}
