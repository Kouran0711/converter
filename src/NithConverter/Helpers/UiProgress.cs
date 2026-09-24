using Microsoft.UI.Dispatching;
using NithConverter.Core.Models;

namespace NithConverter.Helpers;

// Coalesce pending reports: at most one queued UI callback, even when the UI is busy.
public sealed class UiProgress(DispatcherQueue dispatcher, Action<ConversionProgress> update) : IProgress<ConversionProgress>, IDisposable
{
    private readonly object _gate = new();
    private ConversionProgress? _latest;
    private bool _queued;
    private bool _disposed;
    public void Report(ConversionProgress value)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _latest = value;
            if (_queued) return;
            _queued = true;
        }
        if (!dispatcher.TryEnqueue(() =>
        {
            ConversionProgress? next;
            lock (_gate) { _queued = false; next = _disposed ? null : _latest; }
            if (next is not null) update(next);
        })) { lock (_gate) _queued = false; }
    }
    public void Dispose() { lock (_gate) { _disposed = true; _latest = null; } }
}
