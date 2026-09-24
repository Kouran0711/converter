using System.Diagnostics;
using System.Text;

namespace NithConverter.Core.Helpers;

public sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);

/// <summary>Owns each child from start through pipe draining and termination.</summary>
public static class ProcessHelper
{
    public static async Task<ProcessExecutionResult> RunAsync(string executable,
        IEnumerable<string> arguments, CancellationToken cancellationToken,
        Action<string>? outputLine = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true
        };
        foreach (string argument in arguments) startInfo.ArgumentList.Add(argument);
        if (environment is not null)
            foreach (var pair in environment) startInfo.Environment[pair.Key] = pair.Value;

        using var process = new Process { StartInfo = startInfo };
        if (!process.Start()) throw new IOException("Não foi possível iniciar o mecanismo de conversão.");
        using var job = ProcessJob.TryCreate(process);
        process.StandardInput.Close();
        // Lower child priority; CPU intensive work cannot starve the foreground window.
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }

        using var registration = cancellationToken.Register(() =>
        {
            job?.Terminate();
            Kill(process);
        });
        using var pipeLifetime = new CancellationTokenSource();
        Task<string> stdout = DrainAsync(process.StandardOutput, outputLine, pipeLifetime.Token);
        Task<string> stderr = DrainAsync(process.StandardError, null, pipeLifetime.Token);
        try
        {
            Task exited = process.WaitForExitAsync(CancellationToken.None);
            Task<string[]> drained = Task.WhenAll(stdout, stderr);
            Task first = await Task.WhenAny(exited, drained).ConfigureAwait(false);
            if (first == drained && drained.IsFaulted)
            {
                // A failed pipe reader must not leave a producer blocked on a full pipe.
                job?.Terminate();
                Kill(process);
            }
            // Cancellation kills the process tree; waiting without the canceled token reaps it.
            await exited.ConfigureAwait(false);
            // Bound draining if a malfunctioning delegate kept an inherited pipe open.
            pipeLifetime.CancelAfter(TimeSpan.FromSeconds(3));
            string[] output = await drained.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            return new(process.ExitCode, output[0], output[1]);
        }
        finally
        {
            job?.Terminate();
            Kill(process);
            try { await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false); }
            catch (InvalidOperationException) { }
            await pipeLifetime.CancelAsync().ConfigureAwait(false);
            // Always observe both tasks, also if waiting failed.
            try { await Task.WhenAll(stdout, stderr).ConfigureAwait(false); }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        }
    }

    private static async Task<string> DrainAsync(StreamReader reader, Action<string>? lineHandler, CancellationToken token)
    {
        const int limit = 16 * 1024;
        var tail = new StringBuilder(limit);
        var line = new StringBuilder(256);
        var buffer = new char[2048];
        try
        {
            while (true)
            {
                int count = await reader.ReadAsync(buffer.AsMemory(), token).ConfigureAwait(false);
                if (count == 0) break;
                tail.Append(buffer, 0, count);
                if (tail.Length > limit) tail.Remove(0, tail.Length - limit);
                if (lineHandler is null) continue;
                for (int i = 0; i < count; i++)
                {
                    char c = buffer[i];
                    if (c == '\n' || c == '\r')
                    {
                        if (line.Length > 0)
                        {
                            DispatchLine(lineHandler, line.ToString());
                            line.Clear();
                        }
                    }
                    else if (line.Length < 4096) line.Append(c);
                }
            }
            if (line.Length > 0 && lineHandler is not null) DispatchLine(lineHandler, line.ToString());
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        return tail.ToString();
    }

    private static void DispatchLine(Action<string> handler, string line)
    {
        try { handler(line); }
        catch (Exception) { /* Observers must never stop draining a child's redirected pipes. */ }
    }

    private static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
    }
}
