using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

public sealed class HistoryService
{
    public const int MaximumEntries = 100;
    private readonly JsonStore<List<HistoryEntry>> _store;
    public HistoryService(string? dataDirectory = null) => _store = new(
        Path.Combine(dataDirectory ?? StoragePaths.DataDirectory, "history.json"), () => [], 2 * 1024 * 1024);

    public async Task<IReadOnlyList<HistoryEntry>> LoadAsync(CancellationToken cancellationToken = default) =>
        (await _store.ReadAsync(cancellationToken).ConfigureAwait(false)).Where(IsValid).Take(MaximumEntries).ToArray();
    public Task AddAsync(HistoryEntry entry, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(entries => entries.Prepend(entry).Where(IsValid).Take(MaximumEntries).ToList(), cancellationToken);
    public Task ClearAsync(CancellationToken cancellationToken = default) => _store.WriteAsync([], cancellationToken);

    private static bool IsValid(HistoryEntry? entry) => entry is not null
        && !string.IsNullOrWhiteSpace(entry.InputName) && !string.IsNullOrWhiteSpace(entry.OutputName)
        && !string.IsNullOrWhiteSpace(entry.OutputPath) && !string.IsNullOrWhiteSpace(entry.Format);
}
