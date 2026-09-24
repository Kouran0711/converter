using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

/// <summary>Event-driven discovery, cached until explicitly refreshed. Never runs a shell.</summary>
public sealed class DependencyService(string? applicationDirectory = null, bool allowSystemSearch = true)
{
    private readonly string _applicationDirectory = applicationDirectory ?? AppContext.BaseDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private DependencySnapshot? _cached;

    public async Task<DependencySnapshot> DiscoverAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _cached is not null) return _cached;
            _cached = await Task.Run(() => Discover(cancellationToken), cancellationToken).ConfigureAwait(false);
            return _cached;
        }
        finally { _gate.Release(); }
    }

    private DependencySnapshot Discover(CancellationToken token)
    {
        var roots = new List<string>
        {
            Path.Combine(_applicationDirectory, "bin"),
            Path.Combine(_applicationDirectory, "bin", "ImageMagick"),
            Path.Combine(_applicationDirectory, "bin", "FFmpeg"),
            Path.Combine(_applicationDirectory, "bin", "FFmpeg", "bin"),
            Path.Combine(_applicationDirectory, "bin", "Ghostscript", "bin"),
            _applicationDirectory
        };
        roots.AddRange(Children(Path.Combine(_applicationDirectory, "bin")));
        if (allowSystemSearch)
        {
            string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            string programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            foreach (string parent in new[] { programFiles, programFilesX86, Path.Combine(local, "Programs") })
            {
                foreach (string child in Children(parent).Where(IsDependencyDirectory))
                {
                    roots.Add(child);
                    roots.Add(Path.Combine(child, "bin"));
                }
                foreach (string child in Children(Path.Combine(parent, "gs")))
                    roots.Add(Path.Combine(child, "bin"));
            }
            roots.Add(Path.Combine(local, "Microsoft", "WinGet", "Links"));
            string packages = Path.Combine(local, "Microsoft", "WinGet", "Packages");
            foreach (string package in Children(packages).Where(IsDependencyDirectory))
            {
                roots.Add(package);
                roots.Add(Path.Combine(package, "bin"));
                foreach (string child in Children(package))
                {
                    roots.Add(child);
                    roots.Add(Path.Combine(child, "bin"));
                }
            }
            roots.Add(@"C:\ffmpeg\bin");
            roots.Add(@"C:\ProgramData\chocolatey\bin");
            roots.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(path => path.Trim('"')));
        }
        string? magick = null, ffmpeg = null, ffprobe = null, ghostscript = null;
        foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            token.ThrowIfCancellationRequested();
            magick ??= Find(root, "magick.exe");
            ffmpeg ??= Find(root, "ffmpeg.exe");
            ffprobe ??= Find(root, "ffprobe.exe");
            ghostscript ??= Find(root, "gswin64c.exe") ?? Find(root, "gswin32c.exe");
        }
        if (ffmpeg is not null)
            ffprobe = Find(Path.GetDirectoryName(ffmpeg)!, "ffprobe.exe") ?? ffprobe;
        return new(magick, ffmpeg, ffprobe, ghostscript);
    }

    private static bool IsDependencyDirectory(string path)
    {
        string name = Path.GetFileName(path);
        return name.Contains("ImageMagick", StringComparison.OrdinalIgnoreCase)
            || name.Contains("FFmpeg", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Ghostscript", StringComparison.OrdinalIgnoreCase);
    }

    private static string? Find(string directory, string executable)
    {
        try
        {
            string path = Path.GetFullPath(Path.Combine(directory, executable));
            return File.Exists(path) ? path : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return null; }
    }

    private static IEnumerable<string> Children(string directory)
    {
        try
        {
            if (!Directory.Exists(directory)) return [];
            return Directory.EnumerateDirectories(directory).Take(256).ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        { return []; }
    }
}
