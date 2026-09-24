using NithConverter.Core.Helpers;
using NithConverter.Core.Models;

namespace NithConverter.Core.Services;

public sealed class SettingsService
{
    private readonly JsonStore<AppSettings> _store;
    public SettingsService(string? dataDirectory = null) => _store = new(
        Path.Combine(dataDirectory ?? StoragePaths.DataDirectory, "settings.json"), () => new(), 64 * 1024);
    public Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) => _store.ReadAsync(cancellationToken);
    public Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) => _store.WriteAsync(settings, cancellationToken);
    public Task SaveConversionDefaultsAsync(ConversionOptions options, CancellationToken cancellationToken = default) =>
        _store.UpdateAsync(settings => { settings.DefaultConversionOptions = options; return settings; }, cancellationToken);
}
