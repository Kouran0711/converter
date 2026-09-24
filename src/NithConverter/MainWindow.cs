using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using NithConverter.Services;
using NithConverter.ViewModels;
using NithConverter.Views;

namespace NithConverter;

// The window owns only native window lifecycle. Conversion and persistence live in services.
public sealed class MainWindow : Window
{
    private MainViewModel? _viewModel;
    private WindowInteraction? _interaction;
    private bool _closing;
    private bool _allowClose;
    private readonly string? _initialFile;
    private readonly Microsoft.UI.Xaml.Controls.Grid _root = new();
    private SplashView? _splash;
    public MainWindow(string? initialFile = null)
    {
        _initialFile = initialFile;
        Title = "NITH Converter";
        AppWindow.Resize(new Windows.Graphics.SizeInt32(1140, 860));
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "Generated", "app.ico"));
        var splash = _splash = new SplashView();
        _root.Children.Add(splash);
        Content = _root;
        splash.Loaded += SplashLoaded;
        splash.Finished += SplashFinished;
        AppWindow.Closing += OnClosing;
        Closed += (_, _) => _viewModel?.Dispose();
    }
    private void SplashLoaded(object sender, RoutedEventArgs e)
    {
        ((SplashView)sender).Loaded -= SplashLoaded;
        // Give the initial lightweight view a render opportunity; no artificial startup delay.
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, BuildShell);
    }
    private void SplashFinished(object? sender, EventArgs args)
    {
        if (_splash is null) return;
        _splash.Finished -= SplashFinished;
        _root.Children.Remove(_splash);
        _splash = null;
    }
    private async void BuildShell()
    {
        if (_closing) return;
        try
        {
            _interaction = new(this);
            _viewModel = new(_interaction, DispatcherQueue);
            var shell = new ShellView(_viewModel);
            _root.Children.Insert(0, shell);
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                ExtendsContentIntoTitleBar = true;
                SetTitleBar(shell.DragTitleBar);
                AppWindow.TitleBar.ButtonBackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
                AppWindow.TitleBar.ButtonForegroundColor = Colors.White;
                AppWindow.TitleBar.ButtonInactiveForegroundColor = Colors.Gray;
            }
            Task initializeTask = _viewModel.InitializeAsync();
            // Mantém a abertura visível por um instante para a animação da marca, sem bloquear o carregamento real.
            await Task.Delay(950);
            _splash?.Dismiss();
            await initializeTask;
            if (!_closing && !string.IsNullOrWhiteSpace(_initialFile)) await _viewModel.SelectFileAsync(_initialFile);
        }
        catch (Exception ex)
        {
            if (_viewModel is not null) _viewModel.ReportError(ex);
            await new NithConverter.Core.Services.LocalLogger().WriteAsync("startup.failed", ex.GetType().Name);
            var detailsButton = new Microsoft.UI.Xaml.Controls.Button { Content = "Detalhes técnicos" };
            detailsButton.Click += async (_, _) =>
            {
                try { await (_interaction ?? new WindowInteraction(this)).ShowDetailsAsync(ex.ToString()); }
                catch (Exception) { /* A failed error dialog must not cause a second startup failure. */ }
            };
            Content = new Microsoft.UI.Xaml.Controls.StackPanel
            {
                Margin = new Thickness(32), Spacing = 16,
                Children = { new Microsoft.UI.Xaml.Controls.TextBlock { Text = "Não foi possível iniciar a interface. Feche o aplicativo e tente novamente.", TextWrapping = TextWrapping.Wrap }, detailsButton }
            };
        }
    }
    private async void OnClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_allowClose) return;
        args.Cancel = true;
        if (_closing) return;
        _closing = true;
        _interaction?.CloseDialogs();
        try { if (_viewModel is not null) await _viewModel.ShutdownAsync(); }
        finally { _allowClose = true; Close(); }
    }
}
