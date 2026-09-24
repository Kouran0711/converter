using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using NithConverter.Core.Models;
using NithConverter.Core.Services;

// A dependency-free executable harness. A nonzero exit code means at least one real assertion failed.
var artifacts = Path.GetFullPath(Path.Combine("tests", "artifacts", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
Directory.CreateDirectory(artifacts);
var filterIndex = Array.IndexOf(args, "--filter");
var filter = filterIndex >= 0 && filterIndex + 1 < args.Length ? args[filterIndex + 1] : null;
var suite = new IntegrationSuite(artifacts, args.Contains("--full", StringComparer.OrdinalIgnoreCase), filter);
return await suite.RunAsync();

internal sealed class IntegrationSuite(string artifacts, bool full, string? filter)
{
    private readonly List<TestOutcome> results = [];
    private DependencySnapshot dependencies = null!;
    private ConversionService converter = null!;
    private string png = "";
    private string webp = "";
    private string shortVideo = "";
    private string longVideo = "";

    public async Task<int> RunAsync()
    {
        Console.WriteLine($"Artifacts: {artifacts}");
        await CaseAsync("Dependency discovery and fixture generation", PrepareAsync);
        if (converter is not null)
        {
            await CaseAsync("PNG -> JPG; spaces and accents; preserve original", () => ConvertImageAsync(png, "jpg", "JPEG"));
            await CaseAsync("PNG -> PDF; preserve original", ConvertPdfAsync);
            await CaseAsync("WEBP -> PNG; preserve original", () => ConvertImageAsync(webp, "png", "PNG"));
            await CaseAsync("MP4 -> GIF; real progress and valid output", ConvertShortVideoAsync);
            await CaseAsync("Existing destination refused and explicit overwrite succeeds", ExistingOutputAsync);
            await CaseAsync("Destination changed during conversion requires new confirmation", ConcurrentDestinationAsync);
            await CaseAsync("Source cannot be overwritten even with overwrite=true", PreserveSamePathAsync);
            await CaseAsync("Custom output names preserve originals and require overwrite approval", CustomOutputNamesAsync);
            await CaseAsync("Image options: quality, resize and lossless PNG compression", ImageOptionsAsync);
            await CaseAsync("Video options: MP4 and WEBM quality, FPS, size and audio", VideoOptionsAsync);
            await CaseAsync("GIF options: FPS, width and palette colors", GifOptionsAsync);
            await CaseAsync("Conversion options validation and saved defaults", OptionsValidationAsync);
            await CaseAsync("Extended Windows path > 260 characters", LongPathAsync);
            await CaseAsync("Unsupported input has a friendly result", UnsupportedAsync);
            await CaseAsync("Corrupt image returns a friendly error with separate diagnostics", CorruptImageAsync);
            await CaseAsync("Missing ImageMagick and FFmpeg produce DependencyMissing", MissingDependenciesAsync);
            await CaseAsync("Cancellation preserves existing output and leaves no worker", CancelLongVideoAsync);
            await CaseAsync("Repeated canceled conversions leave no processes or temp files", RepeatCancellationAsync);
            if (full)
                await CaseAsync("180-second 1080p video -> GIF with CPU/RAM/progress telemetry", ConvertLongVideoAsync);
        }

        var report = new
        {
            utc = DateTimeOffset.UtcNow,
            os = Environment.OSVersion.ToString(),
            architecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString(),
            framework = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            logicalProcessors = Environment.ProcessorCount,
            full,
            filter,
            tests = results,
            limitation = "Core integration harness only. Scheduler heartbeat is not proof of WinUI responsiveness; verify the actual desktop window separately. Synthetic 180s 1080p fixture repeats 5 seconds and does not cover every codec or very large source file."
        };
        await File.WriteAllTextAsync(Path.Combine(artifacts, "report.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        var failed = results.Count(x => !x.Passed);
        Console.WriteLine($"\n{results.Count - failed}/{results.Count} passed. Report: {Path.Combine(artifacts, "report.json")}");
        return failed == 0 ? 0 : 1;
    }

    private async Task PrepareAsync()
    {
        var dependencyService = new DependencyService(Path.Combine(artifacts, "app"));
        dependencies = await dependencyService.DiscoverAsync();
        Check(!string.IsNullOrWhiteSpace(dependencies.ImageMagickPath), "Real ImageMagick installation is required for this integration suite.");
        Check(!string.IsNullOrWhiteSpace(dependencies.FFmpegPath), "Real FFmpeg installation is required for this integration suite.");
        Check(!string.IsNullOrWhiteSpace(dependencies.FFprobePath), "Real ffprobe installation is required for duration assertions.");
        await File.WriteAllTextAsync(Path.Combine(artifacts, "dependencies.json"), JsonSerializer.Serialize(dependencies, new JsonSerializerOptions { WriteIndented = true }));
        await File.WriteAllTextAsync(Path.Combine(artifacts, "magick-version.txt"), await RunProcessAsync(dependencies.ImageMagickPath!, ["-version"]));
        await File.WriteAllTextAsync(Path.Combine(artifacts, "ffmpeg-version.txt"), await RunProcessAsync(dependencies.FFmpegPath!, ["-version"]));

        png = Path.Combine(artifacts, "restaurante com acentos ação.png");
        webp = Path.Combine(artifacts, "imagem com espaços ç.webp");
        shortVideo = Path.Combine(artifacts, "vídeo curto ação.mp4");
        longVideo = Path.Combine(artifacts, "vídeo longo 180 segundos.mp4");
        await RunProcessAsync(dependencies.ImageMagickPath!, ["-size", "640x360", "gradient:#16283c-#8be0ff", png]);
        await RunProcessAsync(dependencies.ImageMagickPath!, [png, webp]);
        await RunProcessAsync(dependencies.FFmpegPath!, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=640x360:rate=24", "-t", "2", "-c:v", "libx264", "-preset", "ultrafast", "-pix_fmt", "yuv420p", shortVideo]);
        var source = Path.Combine(artifacts, "fixture-1080p-5s.mp4");
        await RunProcessAsync(dependencies.FFmpegPath!, ["-hide_banner", "-loglevel", "error", "-y", "-f", "lavfi", "-i", "testsrc2=size=1920x1080:rate=30", "-t", "5", "-c:v", "libx264", "-preset", "ultrafast", "-crf", "24", "-pix_fmt", "yuv420p", source]);
        await RunProcessAsync(dependencies.FFmpegPath!, ["-hide_banner", "-loglevel", "error", "-y", "-stream_loop", "35", "-i", source, "-t", "180", "-c", "copy", longVideo]);
        var data = Path.Combine(artifacts, "local-data");
        converter = new ConversionService(dependencyService, new HistoryService(data), new LocalLogger(data));
    }

    private async Task ImageOptionsAsync()
    {
        var destination = NewDestination("image-options");
        async Task<string> ConvertWith(string format, string name, ConversionOptions options)
        {
            var result = await converter.ConvertAsync(new(png, format, destination, OutputBaseName: name, Options: options));
            Success(result); return result.OutputPath!;
        }
        var jpeg = await ConvertWith("jpg", "quality-35", new() { ImageQuality = 35, MaxWidth = 320 });
        var info = await RunProcessAsync(dependencies.ImageMagickPath!, ["identify", "-format", "%w %h %Q", jpeg]);
        Check(info.Trim() == "320 180 35", $"JPEG dimensions/quality mismatch: {info}");
        var large = await ConvertWith("jpg", "no-upscale", new() { MaxWidth = 1920 });
        Check((await RunProcessAsync(dependencies.ImageMagickPath!, ["identify", "-format", "%w %h", large])).Trim() == "640 360", "Image was enlarged unexpectedly.");
        var webpLow = await ConvertWith("webp", "webp-low", new() { ImageQuality = 20, MaxWidth = 320 });
        var webpHigh = await ConvertWith("webp", "webp-high", new() { ImageQuality = 95, MaxWidth = 320 });
        Check(await HashAsync(webpLow) != await HashAsync(webpHigh), "WEBP quality did not affect encoding.");
        Check((await RunProcessAsync(dependencies.ImageMagickPath!, ["identify", "-format", "%w %h", webpHigh])).Trim() == "320 180", "WEBP resize failed.");
        var fast = await ConvertWith("png", "png-fast", new() { PngCompression = 0 });
        var compressed = await ConvertWith("png", "png-compressed", new() { PngCompression = 9 });
        var signature1 = await RunProcessAsync(dependencies.ImageMagickPath!, [fast, "-format", "%#", "info:"]);
        var signature2 = await RunProcessAsync(dependencies.ImageMagickPath!, [compressed, "-format", "%#", "info:"]);
        Check(signature1 == signature2, "PNG compression altered decoded pixels.");
        Check(new FileInfo(compressed).Length < new FileInfo(fast).Length, "PNG compression level did not reduce this fixture.");
    }

    private async Task VideoOptionsAsync()
    {
        var input = Path.Combine(artifacts, "video with audio.mp4");
        await RunProcessAsync(dependencies.FFmpegPath!, ["-hide_banner", "-loglevel", "error", "-y", "-i", shortVideo, "-f", "lavfi", "-i", "sine=frequency=440:sample_rate=48000", "-map", "0:v:0", "-map", "1:a:0", "-t", "2", "-c:v", "copy", "-c:a", "aac", input]);
        var destination = NewDestination("video-options");
        foreach (var format in new[] { "MP4", "WEBM" })
        {
            async Task<string> Encode(string name, bool audio, VideoQuality quality)
            {
                var result = await converter.ConvertAsync(new(input, format, destination, OutputBaseName: name,
                    Options: new() { MaxWidth = 320, FramesPerSecond = 15, KeepAudio = audio, VideoQuality = quality }));
                Success(result); return result.OutputPath!;
            }
            var withAudio = await Encode(format + "-audio", true, VideoQuality.Balanced);
            var high = await Encode(format + "-high", false, VideoQuality.High);
            var compact = await Encode(format + "-compact", false, VideoQuality.Compact);
            Check(new FileInfo(high).Length != new FileInfo(compact).Length, "Video quality setting did not change output size.");
            foreach (var (path, expectAudio) in new[] { (withAudio, true), (high, false), (compact, false) })
            {
                using var probe = JsonDocument.Parse(await RunProcessAsync(dependencies.FFprobePath!, ["-v", "error", "-show_entries", "stream=codec_type,width,height,r_frame_rate", "-of", "json", path]));
                var streams = probe.RootElement.GetProperty("streams").EnumerateArray().ToArray();
                var video = streams.First(s => s.GetProperty("codec_type").GetString() == "video");
                Check(video.GetProperty("width").GetInt32() == 320 && video.GetProperty("height").GetInt32() == 180, "Video resolution mismatch.");
                Check(video.GetProperty("r_frame_rate").GetString() == "15/1", "Video FPS mismatch.");
                Check(streams.Any(s => s.GetProperty("codec_type").GetString() == "audio") == expectAudio, "Audio selection mismatch.");
            }
        }
    }

    private async Task GifOptionsAsync()
    {
        var result = await converter.ConvertAsync(new(shortVideo, "gif", NewDestination("gif-options"),
            Options: new() { MaxWidth = 320, FramesPerSecond = 8, GifColors = 64 }));
        Success(result);
        using var probe = JsonDocument.Parse(await RunProcessAsync(dependencies.FFprobePath!, ["-v", "error", "-count_frames", "-show_entries", "stream=width,height,nb_read_frames", "-of", "json", result.OutputPath!]));
        var stream = probe.RootElement.GetProperty("streams")[0];
        Check(stream.GetProperty("width").GetInt32() == 320 && stream.GetProperty("height").GetInt32() == 180, "GIF resolution mismatch.");
        Check(stream.GetProperty("nb_read_frames").GetString() == "16", "GIF 8 FPS should produce 16 frames for the 2-second fixture.");
        var colors = await RunProcessAsync(dependencies.ImageMagickPath!, ["identify", "-format", "%k", result.OutputPath! + "[0]"]);
        Check(int.Parse(colors.Trim()) <= 64, "GIF exceeded requested color count.");
    }

    private async Task OptionsValidationAsync()
    {
        foreach (var options in new ConversionOptions[] { new() { ImageQuality = 0 }, new() { PngCompression = 10 }, new() { MaxWidth = 0 }, new() { FramesPerSecond = 100 }, new() { GifColors = 32 }, new() { VideoQuality = (VideoQuality)999 } })
        {
            var result = await converter.ConvertAsync(new(png, "jpg", NewDestination("invalid-options"), Options: options));
            Check(result.Error == ConversionError.InvalidInput, "Invalid conversion options were accepted.");
        }
        var settings = new SettingsService(NewDestination("options-settings"));
        await settings.SaveAsync(new() { OpenFolderAfterConversion = true });
        var defaults = new ConversionOptions { ImageQuality = 78, MaxWidth = 1280, FramesPerSecond = 24, GifColors = 128, VideoQuality = VideoQuality.High, KeepAudio = false };
        await settings.SaveConversionDefaultsAsync(defaults);
        var saved = await settings.LoadAsync();
        Check(saved.DefaultConversionOptions == defaults && saved.OpenFolderAfterConversion, "Saving conversion defaults lost settings or failed to persist options.");
    }

    private async Task CustomOutputNamesAsync()
    {
        var hash = await HashAsync(png);
        var destination = NewDestination("renamed");
        var request = new ConversionRequest(png, "jpg", destination, OutputBaseName: "meu arquivo ação.v2");
        var result = await converter.ConvertAsync(request);
        Success(result);
        Check(result.OutputPath == Path.Combine(destination, "meu arquivo ação.v2.jpg"), "Custom name or format extension was lost.");
        var outputHash = await HashAsync(result.OutputPath!);
        var duplicate = await converter.ConvertAsync(request);
        Check(duplicate.Error == ConversionError.OutputExists, "Renamed output bypassed overwrite confirmation.");
        Check(outputHash == await HashAsync(result.OutputPath!), "Existing renamed output changed without approval.");
        Success(await converter.ConvertAsync(request with { Overwrite = true }));
        foreach (var invalid in new[] { "", "  ", "../escape", "..\\escape", "C:\\escape", "file:stream", "CON", "nul.txt", "COM1", "LPT9.txt", "bad?name", "trailing.", "trailing " })
        {
            var rejected = await converter.ConvertAsync(request with { OutputBaseName = invalid });
            Check(rejected.Error == ConversionError.InvalidInput, $"Invalid custom output name accepted: {invalid}");
        }
        var sameSource = new ConversionRequest(png, "png", OutputBaseName: Path.GetFileNameWithoutExtension(png));
        Check(!string.Equals(converter.GetOutputPath(sameSource), png, StringComparison.OrdinalIgnoreCase), "Custom name can target the original.");
        Check(hash == await HashAsync(png), "Renaming changed source contents.");
    }

    private async Task ConvertImageAsync(string input, string format, string signature, string? destinationDirectory = null)
    {
        var hash = await HashAsync(input);
        var destination = destinationDirectory ?? NewDestination(format);
        var result = await converter.ConvertAsync(new ConversionRequest(input, format, destination));
        Success(result);
        var identified = await RunProcessAsync(dependencies.ImageMagickPath!, ["identify", "-format", "%m %w %h", result.OutputPath!]);
        Check(identified.Trim() == $"{signature} 640 360", $"Unexpected image output: {identified}");
        Check(hash == await HashAsync(input), "Source contents changed.");
    }

    private async Task ConvertPdfAsync()
    {
        var hash = await HashAsync(png);
        var result = await converter.ConvertAsync(new ConversionRequest(png, "pdf", NewDestination("pdf")));
        Success(result);
        await using var stream = File.OpenRead(result.OutputPath!);
        var magic = new byte[5];
        await stream.ReadExactlyAsync(magic);
        Check(Encoding.ASCII.GetString(magic) == "%PDF-", "Invalid PDF header.");
        Check(hash == await HashAsync(png), "PNG source contents changed.");
    }

    private async Task ConvertShortVideoAsync()
    {
        var hash = await HashAsync(shortVideo);
        var updates = new ProgressRecorder();
        var result = await converter.ConvertAsync(new ConversionRequest(shortVideo, "gif", NewDestination("short-gif")), updates);
        Success(result);
        await VerifyVideoAsync(result.OutputPath!, "gif", 1.5);
        updates.Verify();
        Check(hash == await HashAsync(shortVideo), "Video source contents changed.");
    }

    private async Task ExistingOutputAsync()
    {
        var request = new ConversionRequest(png, "jpg", NewDestination("overwrite"));
        var output = converter.GetOutputPath(request);
        await File.WriteAllTextAsync(output, "Existing content must be preserved until success.");
        var original = await HashAsync(output);
        var declined = await converter.ConvertAsync(request);
        Check(!declined.Success && declined.Error == ConversionError.OutputExists, $"Expected OutputExists, got {declined.Error}.");
        Check(original == await HashAsync(output), "Declined overwrite changed the destination.");
        var replaced = await converter.ConvertAsync(request with { Overwrite = true });
        Success(replaced);
        Check(original != await HashAsync(output), "Explicit overwrite did not replace destination.");
        Check((await RunProcessAsync(dependencies.ImageMagickPath!, ["identify", "-format", "%m", output])).Trim() == "JPEG", "Replacement is invalid.");
    }

    private async Task PreserveSamePathAsync()
    {
        var hash = await HashAsync(png);
        var result = await converter.ConvertAsync(new ConversionRequest(png, "png", Path.GetDirectoryName(png), true));
        Check(hash == await HashAsync(png), "Original was replaced by a same-path conversion.");
        Check(!result.Success || !string.Equals(result.OutputPath, png, StringComparison.OrdinalIgnoreCase), "Same path reported success and overwrote the source.");
    }

    private async Task ConcurrentDestinationAsync()
    {
        var request = new ConversionRequest(png, "jpg", NewDestination("concurrent-output"), true);
        var output = converter.GetOutputPath(request);
        await File.WriteAllTextAsync(output, "Original target before confirmation.");
        const string updated = "Another program wrote this after confirmation. It must remain intact.";
        var progress = new CallbackProgress(() => File.WriteAllText(output, updated));
        var result = await converter.ConvertAsync(request, progress);
        Check(result.Error == ConversionError.OutputExists, $"Expected new confirmation after concurrent write, got {result.Error}.");
        Check(await File.ReadAllTextAsync(output) == updated, "Concurrent change was silently overwritten.");
        Check(Directory.GetFileSystemEntries(request.DestinationDirectory!).Length == 1, "Concurrent conflict left temporary output.");
    }

    private async Task LongPathAsync()
    {
        var directory = NewDestination("long-path");
        while (directory.Length < 280)
            directory = Path.Combine(directory, "pasta longa com espaços ação");
        Directory.CreateDirectory(directory);
        var input = Path.Combine(directory, "entrada á.png");
        await using (var source = File.OpenRead(png))
        await using (var target = File.Create(input))
            await source.CopyToAsync(target);
        Check(input.Length > 260, "Long-path fixture was too short.");
        await ConvertImageAsync(input, "jpg", "JPEG", directory);
    }

    private async Task UnsupportedAsync()
    {
        var input = Path.Combine(artifacts, "unsupported.txt");
        await File.WriteAllTextAsync(input, "Unsupported input.");
        var result = await converter.ConvertAsync(new ConversionRequest(input, "png", NewDestination("unsupported")));
        Check(!result.Success && result.Error == ConversionError.UnsupportedFormat, $"Expected unsupported format, got {result.Error}.");
        Check(!string.IsNullOrWhiteSpace(result.UserMessage), "User message is missing.");
    }

    private async Task MissingDependenciesAsync()
    {
        // Isolated discovery is intentionally supplied by the production service for deterministic deployment tests.
        var missing = new DependencyService(Path.Combine(artifacts, "missing-app"), allowSystemSearch: false);
        var data = NewDestination("missing-data");
        var service = new ConversionService(missing, new HistoryService(data), new LocalLogger(data));
        foreach (var (input, format) in new[] { (png, "jpg"), (shortVideo, "gif") })
        {
            var result = await service.ConvertAsync(new ConversionRequest(input, format, NewDestination("missing-output")));
            Check(!result.Success && result.Error == ConversionError.DependencyMissing, $"Expected DependencyMissing for {format}, got {result.Error}.");
            Check(!string.IsNullOrWhiteSpace(result.UserMessage), "Missing dependency has no friendly explanation.");
        }
    }

    private async Task CorruptImageAsync()
    {
        var input = Path.Combine(artifacts, "corrupt image.png");
        await File.WriteAllTextAsync(input, "This is not a PNG image.");
        var destination = NewDestination("corrupt");
        var result = await converter.ConvertAsync(new ConversionRequest(input, "jpg", destination));
        Check(!result.Success && result.Error == ConversionError.Failed, $"Corrupt input should fail, got {result.Error}.");
        Check(!string.IsNullOrWhiteSpace(result.TechnicalDetails), "Technical diagnostics were lost.");
        Check(!result.UserMessage.Contains("@ error/", StringComparison.Ordinal), "Raw ImageMagick diagnostic leaked into friendly message.");
        Check(Directory.GetFileSystemEntries(destination).Length == 0, "Failed conversion left temporary output.");
    }

    private async Task CancelLongVideoAsync()
    {
        var request = new ConversionRequest(longVideo, "gif", NewDestination("cancel"), true);
        var output = converter.GetOutputPath(request);
        await File.WriteAllTextAsync(output, "Previously converted result remains intact when cancellation occurs.");
        var outputHash = await HashAsync(output);
        var inputHash = await HashAsync(longVideo);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        var telemetry = new ConversionTelemetry();
        var progress = new ProgressRecorder();
        var result = await telemetry.ObserveAsync(() => converter.ConvertAsync(request, progress, cancellation.Token));
        await telemetry.SaveAsync(Path.Combine(artifacts, "cancel-telemetry.json"), progress.Count);
        Check(result.Canceled && !result.Success, $"Expected canceled result, got {result.Error}: {result.UserMessage}");
        Check(outputHash == await HashAsync(output), "Cancellation changed an existing destination.");
        Check(inputHash == await HashAsync(longVideo), "Cancellation changed the source.");
        Check(telemetry.Elapsed.TotalSeconds < 15, $"Cancellation took too long: {telemetry.Elapsed}.");
        progress.Verify();
        telemetry.VerifyNoWorkers();
        Check(Directory.GetFileSystemEntries(request.DestinationDirectory!).Length == 1, "Cancellation left temporary output files or directories.");
    }

    private async Task RepeatCancellationAsync()
    {
        var ownProcess = Process.GetCurrentProcess();
        var startHandles = ownProcess.HandleCount;
        var samples = new List<object>();
        for (var i = 0; i < 3; i++)
        {
            var request = new ConversionRequest(longVideo, "gif", NewDestination($"cancel-repeat-{i}"));
            using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1.5));
            var telemetry = new ConversionTelemetry();
            var result = await telemetry.ObserveAsync(() => converter.ConvertAsync(request, cancellationToken: cancellation.Token));
            Check(result.Canceled, $"Repeat #{i + 1} did not cancel.");
            telemetry.VerifyNoWorkers();
            Check(Directory.GetFileSystemEntries(request.DestinationDirectory!).Length == 0, "Repeat cancellation left temp files or directories.");
            ownProcess.Refresh();
            samples.Add(new { iteration = i + 1, ownProcess.HandleCount, ownProcess.WorkingSet64, managedBytes = GC.GetTotalMemory(false) });
        }
        ownProcess.Refresh();
        Check(ownProcess.HandleCount - startHandles < 100, "Excessive handle growth during three canceled jobs.");
        await File.WriteAllTextAsync(Path.Combine(artifacts, "repeat-cancel-metrics.json"), JsonSerializer.Serialize(samples, new JsonSerializerOptions { WriteIndented = true }));
        ownProcess.Dispose();
    }

    private async Task ConvertLongVideoAsync()
    {
        var inputHash = await HashAsync(longVideo);
        var telemetry = new ConversionTelemetry();
        var progress = new ProgressRecorder();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(12));
        var result = await telemetry.ObserveAsync(() => converter.ConvertAsync(new ConversionRequest(longVideo, "gif", NewDestination("long-gif")), progress, timeout.Token));
        await telemetry.SaveAsync(Path.Combine(artifacts, "long-video-telemetry.json"), progress.Count);
        Success(result);
        await VerifyVideoAsync(result.OutputPath!, "gif", 175);
        Check(inputHash == await HashAsync(longVideo), "Large video source changed.");
        progress.Verify();
        progress.VerifyIntermediateProgress();
        telemetry.VerifyNoWorkers();
    }

    private async Task VerifyVideoAsync(string path, string codec, double minimumDuration)
    {
        var json = await RunProcessAsync(dependencies.FFprobePath!, ["-v", "error", "-show_entries", "stream=codec_name:format=duration", "-of", "json", path]);
        using var document = JsonDocument.Parse(json);
        Check(document.RootElement.GetProperty("streams")[0].GetProperty("codec_name").GetString() == codec, "Unexpected output codec.");
        var duration = double.Parse(document.RootElement.GetProperty("format").GetProperty("duration").GetString()!, System.Globalization.CultureInfo.InvariantCulture);
        Check(duration >= minimumDuration, $"Output duration truncated: {duration} seconds.");
    }

    private string NewDestination(string name)
    {
        var path = Path.Combine(artifacts, name + "-" + Guid.NewGuid().ToString("N")[..6]);
        Directory.CreateDirectory(path);
        return path;
    }

    private async Task CaseAsync(string name, Func<Task> test)
    {
        if (filter is not null && !name.Contains("fixture generation", StringComparison.OrdinalIgnoreCase)
            && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
        var timer = Stopwatch.StartNew();
        try
        {
            await test();
            results.Add(new(name, true, timer.Elapsed.TotalSeconds, null));
            Console.WriteLine($"PASS {name} ({timer.Elapsed.TotalSeconds:F2}s)");
        }
        catch (Exception exception)
        {
            results.Add(new(name, false, timer.Elapsed.TotalSeconds, exception.ToString()));
            Console.WriteLine($"FAIL {name}: {exception.Message}");
        }
    }

    private static void Success(ConversionResult result) => Check(result.Success && !result.Canceled && File.Exists(result.OutputPath), $"Conversion failed: {result.Error}: {result.UserMessage}\n{result.TechnicalDetails}");
    internal static void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private static async Task<string> HashAsync(string path)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream));
    }

    private static async Task<string> RunProcessAsync(string executable, IEnumerable<string> arguments)
    {
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { CreateNoWindow = true, UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
        foreach (var argument in arguments) process.StartInfo.ArgumentList.Add(argument);
        process.Start();
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        var error = await stderr;
        var output = await stdout;
        Check(process.ExitCode == 0, $"{Path.GetFileName(executable)} exited {process.ExitCode}: {error}");
        return output;
    }

    private sealed record TestOutcome(string Name, bool Passed, double Seconds, string? Failure);
}

internal sealed class CallbackProgress(Action callback) : IProgress<ConversionProgress>
{
    private int called;
    public void Report(ConversionProgress value) { if (Interlocked.Exchange(ref called, 1) == 0) callback(); }
}

internal sealed class ProgressRecorder : IProgress<ConversionProgress>
{
    private readonly List<(long Timestamp, double? Percent)> samples = [];
    public int Count { get { lock (samples) return samples.Count; } }
    public void Report(ConversionProgress value) { lock (samples) samples.Add((Stopwatch.GetTimestamp(), value.Percent)); }
    public void VerifyIntermediateProgress()
    {
        lock (samples)
            IntegrationSuite.Check(samples.Any(x => x.Percent is > 0 and < 100), "No real intermediate percentage was reported for a long video with known duration.");
    }
    public void Verify()
    {
        lock (samples)
        {
            IntegrationSuite.Check(samples.Count > 0, "No progress/status updates were reported.");
            IntegrationSuite.Check(samples.All(x => x.Percent is null || x.Percent is >= 0 and <= 100), "Progress outside 0..100.");
            if (samples.Count > 10)
            {
                var elapsed = Stopwatch.GetElapsedTime(samples[0].Timestamp, samples[^1].Timestamp).TotalSeconds;
                IntegrationSuite.Check(samples.Count / Math.Max(1, elapsed) < 15, "Progress updates exceed 15/second.");
            }
        }
    }
}

internal sealed class ConversionTelemetry
{
    private readonly HashSet<int> baseline = WorkerProcesses().Select(p => { using (p) return p.Id; }).ToHashSet();
    private readonly HashSet<int> observedWorkers = [];
    private readonly List<object> samples = [];
    private long peakWorkerBytes;
    private long peakHarnessBytes;
    private double workerCpuSeconds;
    private double maxSchedulerDelayMilliseconds;
    public TimeSpan Elapsed { get; private set; }

    public async Task<ConversionResult> ObserveAsync(Func<Task<ConversionResult>> action)
    {
        var timer = Stopwatch.StartNew();
        using var stop = new CancellationTokenSource();
        var monitor = MonitorAsync(stop.Token);
        try { return await action(); }
        finally { stop.Cancel(); await monitor; Elapsed = timer.Elapsed; }
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        var cpuByWorker = new Dictionary<int, double>();
        var previous = Stopwatch.GetTimestamp();
        try
        {
            while (!token.IsCancellationRequested)
            {
                using var own = Process.GetCurrentProcess();
                peakHarnessBytes = Math.Max(peakHarnessBytes, own.WorkingSet64);
                long bytes = 0;
                foreach (var process in WorkerProcesses())
                {
                    using (process)
                    {
                        if (baseline.Contains(process.Id)) continue;
                        try
                        {
                            observedWorkers.Add(process.Id);
                            bytes += process.WorkingSet64;
                            cpuByWorker[process.Id] = process.TotalProcessorTime.TotalSeconds;
                        }
                        catch (InvalidOperationException) { }
                    }
                }
                workerCpuSeconds = cpuByWorker.Values.Sum();
                peakWorkerBytes = Math.Max(peakWorkerBytes, bytes);
                var actualInterval = Stopwatch.GetElapsedTime(previous).TotalMilliseconds;
                maxSchedulerDelayMilliseconds = Math.Max(maxSchedulerDelayMilliseconds, Math.Max(0, actualInterval - 500));
                samples.Add(new { utc = DateTimeOffset.UtcNow, workerBytes = bytes, harnessBytes = own.WorkingSet64, workerCpuSeconds });
                previous = Stopwatch.GetTimestamp();
                await Task.Delay(500, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public void VerifyNoWorkers()
    {
        foreach (var process in WorkerProcesses())
        {
            using (process)
                IntegrationSuite.Check(!observedWorkers.Contains(process.Id) || process.HasExited, $"Worker process {process.ProcessName}/{process.Id} is still running after completion/cancellation.");
        }
    }

    public Task SaveAsync(string path, int progressUpdates) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(new
    {
        elapsedSeconds = Elapsed.TotalSeconds,
        peakWorkerWorkingSetMiB = peakWorkerBytes / 1024d / 1024,
        peakHarnessWorkingSetMiB = peakHarnessBytes / 1024d / 1024,
        sampledWorkerCpuSeconds = workerCpuSeconds,
        normalizedWorkerCpuPercent = workerCpuSeconds / Math.Max(0.001, Elapsed.TotalSeconds) / Environment.ProcessorCount * 100,
        maxSchedulerDelayMilliseconds,
        progressUpdates,
        observedWorkers,
        sampleIntervalMilliseconds = 500,
        notes = "CPU is sampled process time and may miss final work between samples. Working set is not private bytes. Scheduler delay measures this console harness only, not WinUI frame responsiveness. Only descendants of this harness are monitored; unrelated workers on the same machine are excluded.",
        samples
    }, new JsonSerializerOptions { WriteIndented = true }));

    private static IEnumerable<Process> WorkerProcesses()
    {
        var owned = NativeProcessTree.GetDescendants(Environment.ProcessId);
        foreach (var process in new[] { "ffmpeg", "ffprobe", "magick" }.SelectMany(Process.GetProcessesByName))
        {
            if (owned.Contains(process.Id)) yield return process;
            else process.Dispose();
        }
    }
}

internal static class NativeProcessTree
{
    public static HashSet<int> GetDescendants(int parentId)
    {
        var parents = new Dictionary<int, int>();
        using var snapshot = CreateToolhelp32Snapshot(2, 0);
        if (snapshot.IsInvalid) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        var entry = new ProcessEntry { Size = (uint)Marshal.SizeOf<ProcessEntry>() };
        if (Process32First(snapshot, ref entry))
        {
            do { parents[(int)entry.ProcessId] = (int)entry.ParentProcessId; }
            while (Process32Next(snapshot, ref entry));
        }
        var descendants = new HashSet<int>();
        foreach (var processId in parents.Keys)
        {
            var ancestor = processId;
            var visited = new HashSet<int>();
            while (visited.Add(ancestor) && parents.TryGetValue(ancestor, out ancestor))
            {
                if (ancestor == parentId) { descendants.Add(processId); break; }
            }
        }
        return descendants;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public UIntPtr DefaultHeap;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int BasePriority;
        public uint Flags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Executable;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern SafeFileHandle CreateToolhelp32Snapshot(uint flags, uint processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32First(SafeFileHandle snapshot, ref ProcessEntry entry);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)] private static extern bool Process32Next(SafeFileHandle snapshot, ref ProcessEntry entry);
}
