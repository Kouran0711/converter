using System.Text;
using NithConverter.Core.Helpers;

namespace NithConverter.Core.Services;

/// <summary>Small rotating local log. Callers supply event codes, never paths or process output.</summary>
public sealed class LocalLogger
{
    private readonly string _directory;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private const long MaxBytes = 512 * 1024;

    public LocalLogger(string? dataDirectory = null) =>
        _directory = Path.Combine(dataDirectory ?? StoragePaths.DataDirectory, "logs");

    public async Task WriteAsync(string eventName, string? detail = null)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                Directory.CreateDirectory(_directory);
                string path = Path.Combine(_directory, "app.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxBytes)
                    File.Move(path, path + ".1", true);
                string line = $"{DateTimeOffset.UtcNow:O} {Clean(eventName)} {Clean(detail ?? "")}\n";
                await File.AppendAllTextAsync(path, line, Encoding.UTF8).ConfigureAwait(false);
            }).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Diagnostics must never prevent a conversion or crash shutdown.
        }
        finally { _gate.Release(); }
    }

    private static string Clean(string text) => text.Replace('\r', ' ').Replace('\n', ' ')[..Math.Min(text.Length, 256)];
}
