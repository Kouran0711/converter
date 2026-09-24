using System.Text.Json;

namespace NithConverter.Core.Services;

internal sealed class JsonStore<T>(string path, Func<T> defaultFactory, long maxBytes)
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = false };

    public async Task<T> ReadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { return await Task.Run(() => ReadCoreAsync(cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task WriteAsync(T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await Task.Run(() => WriteCoreAsync(value, cancellationToken), cancellationToken).ConfigureAwait(false); }
        finally { _gate.Release(); }
    }

    public async Task UpdateAsync(Func<T, T> update, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await Task.Run(async () =>
            {
                T current = await ReadCoreAsync(cancellationToken).ConfigureAwait(false);
                await WriteCoreAsync(update(current), cancellationToken).ConfigureAwait(false);
            }, cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task<T> ReadCoreAsync(CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > maxBytes) return defaultFactory();
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, token).ConfigureAwait(false) ?? defaultFactory();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        { return defaultFactory(); }
    }

    private async Task WriteCoreAsync(T value, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 16 * 1024, FileOptions.Asynchronous))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, token).ConfigureAwait(false);
                await stream.FlushAsync(token).ConfigureAwait(false);
            }
            token.ThrowIfCancellationRequested();
            File.Move(temporary, path, true);
        }
        finally
        {
            try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
