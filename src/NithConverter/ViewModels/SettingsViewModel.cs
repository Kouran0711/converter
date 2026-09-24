using NithConverter.Core.Models;
using NithConverter.Core.Services;
using NithConverter.Helpers;
using NithConverter.Services;

namespace NithConverter.ViewModels;

public sealed class SettingsViewModel : ObservableObject
{
    private readonly SettingsService _service;
    private readonly CancellationToken _lifetime;
    private string _defaultOutputDirectory = "";
    private bool _openFolder;
    private bool _confirmOverwrite = true;
    private bool _checkForUpdatesOnStartup = true;
    private bool _autoDownloadUpdates = true;
    private string _saveStatus = "As alterações valem para a próxima conversão.";

    public string DefaultOutputDirectory { get => _defaultOutputDirectory; set => Set(ref _defaultOutputDirectory, value); }
    public bool OpenFolderAfterConversion { get => _openFolder; set => Set(ref _openFolder, value); }
    public bool ConfirmOverwrite { get => _confirmOverwrite; set => Set(ref _confirmOverwrite, value); }
    public bool CheckForUpdatesOnStartup { get => _checkForUpdatesOnStartup; set => Set(ref _checkForUpdatesOnStartup, value); }
    public bool AutoDownloadUpdates { get => _autoDownloadUpdates; set => Set(ref _autoDownloadUpdates, value); }
    public string SaveStatus { get => _saveStatus; private set => Set(ref _saveStatus, value); }
    public AsyncCommand BrowseCommand { get; }
    public AsyncCommand SaveCommand { get; }
    public ConversionOptions DefaultConversionOptions { get; private set; } = new();

    public SettingsViewModel(SettingsService service, IWindowInteraction window, CancellationToken lifetime, Action<Exception> error)
    {
        _service = service;
        _lifetime = lifetime;
        BrowseCommand = new(async () => { var path = await window.PickFolderAsync(); if (path is not null) DefaultOutputDirectory = path; }, error);
        SaveCommand = new(SaveAsync, error);
    }

    public async Task LoadAsync()
    {
        var settings = await _service.LoadAsync(_lifetime);
        DefaultOutputDirectory = settings.DefaultOutputDirectory ?? "";
        OpenFolderAfterConversion = settings.OpenFolderAfterConversion;
        ConfirmOverwrite = settings.ConfirmOverwrite;
        CheckForUpdatesOnStartup = settings.CheckForUpdatesOnStartup;
        AutoDownloadUpdates = settings.AutoDownloadUpdates;
        DefaultConversionOptions = settings.DefaultConversionOptions is { } defaults && defaults.GetError() is null ? defaults : new();
    }

    public async Task SaveConversionDefaultsAsync(ConversionOptions options)
    {
        await _service.SaveConversionDefaultsAsync(options, _lifetime);
        DefaultConversionOptions = options;
    }

    private async Task SaveAsync()
    {
        var directory = DefaultOutputDirectory.Trim();
        if (directory.Length > 0 && !await Task.Run(() => Path.IsPathFullyQualified(directory) && Directory.Exists(directory), _lifetime))
        { SaveStatus = "Escolha uma pasta existente com caminho completo."; return; }
        await _service.SaveAsync(new AppSettings
        {
            DefaultOutputDirectory = directory.Length == 0 ? null : directory,
            OpenFolderAfterConversion = OpenFolderAfterConversion,
            ConfirmOverwrite = ConfirmOverwrite,
            CheckForUpdatesOnStartup = CheckForUpdatesOnStartup,
            AutoDownloadUpdates = AutoDownloadUpdates,
            DefaultConversionOptions = DefaultConversionOptions
        }, _lifetime);
        SaveStatus = "Configurações salvas neste computador.";
    }
}
