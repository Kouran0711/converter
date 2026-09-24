using System.Windows.Input;

namespace NithConverter.Helpers;

public sealed class AsyncCommand(Func<Task> execute, Action<Exception> onError, Func<bool>? canExecute = null) : ICommand
{
    private bool _executing;
    public Task ExecutionTask { get; private set; } = Task.CompletedTask;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && (canExecute?.Invoke() ?? true);
    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _executing = true;
        Notify();
        ExecutionTask = RunAsync();
        await ExecutionTask;
    }
    private async Task RunAsync()
    {
        try { await execute(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { onError(ex); }
        finally { _executing = false; Notify(); }
    }
    public void Notify() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
