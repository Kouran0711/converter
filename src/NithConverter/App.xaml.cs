using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;

namespace NithConverter;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Permite ao Windows Restart Manager / Inno Setup reabrir o aplicativo após uma atualização.
        try { _ = RegisterApplicationRestart("--updated", 0); } catch (Exception) { }

        string? initialFile = Environment.GetCommandLineArgs().Skip(1)
            .FirstOrDefault(argument => !argument.StartsWith("--", StringComparison.Ordinal));
        _window = new MainWindow(initialFile);
        _window.Activate();
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterApplicationRestart(string? commandLineArgs, int flags);
}
