using System.Collections.ObjectModel;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NithConverter.Core.Models;
using NithConverter.Core.Services;
using NithConverter.Helpers;
using NithConverter.Services;

namespace NithConverter.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IWindowInteraction _window;
    private readonly DispatcherQueue _dispatcher;
    private readonly DependencyService _dependencies = new();
    private readonly HistoryService _history = new();
    private readonly LocalLogger _logger = new();
    private readonly GitHubUpdateService _updates = new(AppInfo.GitHubOwner, AppInfo.GitHubRepository);
    private readonly ConversionService _converter;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _conversionCancellation;
    private Task? _conversionTask;
    private Task? _initialization;
    private Task? _updateTask;
    private string? _inputPath;
    private string? _destination;
    private string? _lastOutput;
    private int _selectionVersion;
    private string _fileName = "Solte seu arquivo aqui";
    private string _fileDescription = "ou escolha um arquivo no computador";
    private string _outputBaseName = "";
    private string _status = "Pronto para começar";
    private string _statusDetail = "Escolha um arquivo e o formato de saída.";
    private string _technicalDetails = "";
    private string _dependencyStatus = "Verificando mecanismos em segundo plano…";
    private string _imageMagickStatus = "Verificando…";
    private string _ffmpegStatus = "Verificando…";
    private string _pdfStatus = "Verificando…";
    private IReadOnlyList<OutputFormat> _formats = [];
    private OutputFormat? _selectedFormat;
    private bool _busy;
    private bool _indeterminate = true;
    private double _progress;
    private InfoBarSeverity _severity = InfoBarSeverity.Informational;
    private string _optionsStatus = "Ajustes aplicados à próxima conversão.";
    private string _updateStatus = "Atualizações automáticas prontas.";
    private string _updateDetail = "O aplicativo verifica releases oficiais do GitHub e valida o instalador com SHA-256.";
    private string _updateActionText = "";
    private double _updateProgress;
    private bool _updateBannerVisible;
    private bool _updateActionVisible;
    private UpdateRelease? _pendingUpdate;
    private DownloadedUpdate? _downloadedUpdate;

    public MainViewModel(IWindowInteraction window, DispatcherQueue dispatcher)
    {
        _window = window;
        _dispatcher = dispatcher;
        _converter = new(_dependencies, _history, _logger);
        Settings = new(new SettingsService(), window, _lifetime.Token, ReportError);
        ChooseFileCommand = new(async () => { var path = await _window.PickFileAsync(); if (path is not null) await SelectFileAsync(path); }, ReportError, () => !IsBusy);
        ChooseDestinationCommand = new(async () => { var path = await _window.PickFolderAsync(); if (path is not null) { _destination = path; Raise(nameof(DestinationLabel)); } }, ReportError, () => !IsBusy);
        ResetDestinationCommand = new(() => { _destination = null; Raise(nameof(DestinationLabel)); return Task.CompletedTask; }, ReportError, () => !IsBusy);
        ConvertCommand = new(StartConversionAsync, ReportError, () => !IsBusy && _inputPath is not null && SelectedFormat is not null && OutputNameRules.GetError(OutputBaseName) is null);
        CancelCommand = new(CancelAsync, ReportError, () => IsBusy);
        CheckDependenciesCommand = new(() => CheckDependenciesAsync(true), ReportError);
        DetailsCommand = new(() => _window.ShowDetailsAsync(_technicalDetails), ReportError);
        OpenOutputCommand = new(async () => { if (_lastOutput is not null) await _window.OpenFolderAsync(Path.GetDirectoryName(_lastOutput)!); }, ReportError, () => _lastOutput is not null);
        ClearHistoryCommand = new(async () => { await _history.ClearAsync(_lifetime.Token); History.Clear(); Raise(nameof(HistoryEmptyVisibility)); }, ReportError, () => !IsBusy);
        SaveOptionsCommand = new(async () => { await Settings.SaveConversionDefaultsAsync(Options.Snapshot()); OptionsStatus = "Preferências salvas para as próximas aberturas."; }, ReportError);
        ResetOptionsCommand = new(() => { Options.Apply(new()); OptionsStatus = "Ajustes recomendados restaurados."; return Task.CompletedTask; }, ReportError);
        CheckUpdatesCommand = new(() => CheckForUpdatesAsync(force: true), ReportError, () => !IsBusy);
        UpdateActionCommand = new(UpdateActionAsync, ReportError, () => !IsBusy && _updateActionVisible);
        Settings.PropertyChanged += SettingsChanged;
    }

    public SettingsViewModel Settings { get; }
    public ConversionOptionsViewModel Options { get; } = new();
    public string OptionsStatus { get => _optionsStatus; private set => Set(ref _optionsStatus, value); }
    public AsyncCommand SaveOptionsCommand { get; }
    public AsyncCommand ResetOptionsCommand { get; }
    public AsyncCommand CheckUpdatesCommand { get; }
    public AsyncCommand UpdateActionCommand { get; }
    public string AppVersionText => $"Versão {AppInfo.VersionText}";
    public string ProductVersionText => $"NITH Converter {AppInfo.VersionText} · Nith Digital · processamento local";
    public string SignatureText => AppInfo.Signature;
    public string UpdateStatus { get => _updateStatus; private set => Set(ref _updateStatus, value); }
    public string UpdateDetail { get => _updateDetail; private set => Set(ref _updateDetail, value); }
    public string UpdateActionText { get => _updateActionText; private set => Set(ref _updateActionText, value); }
    public double UpdateProgress { get => _updateProgress; private set => Set(ref _updateProgress, value); }
    public Visibility UpdateBannerVisibility => _updateBannerVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UpdateActionVisibility => _updateActionVisible ? Visibility.Visible : Visibility.Collapsed;
    public ObservableCollection<HistoryEntry> History { get; } = [];
    public string FileName { get => _fileName; private set => Set(ref _fileName, value); }
    public string FileDescription { get => _fileDescription; private set => Set(ref _fileDescription, value); }
    public string OutputBaseName
    {
        get => _outputBaseName;
        set
        {
            if (!Set(ref _outputBaseName, value)) return;
            Raise(nameof(OutputNameError)); Raise(nameof(OutputNameErrorVisibility));
            ConvertCommand.Notify();
        }
    }
    public bool CanRename => IsIdle && _inputPath is not null;
    public string OutputExtension => SelectedFormat?.Extension ?? "";
    public string OutputNameError => _inputPath is null ? "" : OutputNameRules.GetError(OutputBaseName) ?? "";
    public Visibility OutputNameErrorVisibility => OutputNameError.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public string DestinationLabel => _destination ?? (string.IsNullOrWhiteSpace(Settings.DefaultOutputDirectory) ? "Mesma pasta do arquivo original" : Settings.DefaultOutputDirectory);
    public string FilePath => _inputPath ?? "Nenhum arquivo selecionado";
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string StatusDetail { get => _statusDetail; private set => Set(ref _statusDetail, value); }
    public InfoBarSeverity Severity { get => _severity; private set => Set(ref _severity, value); }
    public string DependencyStatus { get => _dependencyStatus; private set => Set(ref _dependencyStatus, value); }
    public string ImageMagickStatus { get => _imageMagickStatus; private set => Set(ref _imageMagickStatus, value); }
    public string FFmpegStatus { get => _ffmpegStatus; private set => Set(ref _ffmpegStatus, value); }
    public string PdfStatus { get => _pdfStatus; private set => Set(ref _pdfStatus, value); }
    public IReadOnlyList<OutputFormat> Formats { get => _formats; private set => Set(ref _formats, value); }
    public OutputFormat? SelectedFormat { get => _selectedFormat; set { if (Set(ref _selectedFormat, value)) { ConvertCommand.Notify(); Raise(nameof(ConversionHint)); Raise(nameof(OutputExtension)); Options.Configure(FormatCatalog.GetInputKind(_inputPath ?? ""), value?.Id); } } }
    public string ConversionHint => FormatCatalog.GetInputKind(_inputPath ?? "") switch
    {
        MediaKind.Document => "Documento: a primeira página será convertida. PDF/PS/EPS exigem Ghostscript e permitem escolher o DPI nos ajustes.",
        MediaKind.Audio => "Áudio: converta entre MP3, WAV, FLAC, AAC, M4A, OGG, OPUS e WMA com controle de bitrate, taxa e canais.",
        MediaKind.Video when FormatCatalog.IsAudioOutput(SelectedFormat?.Id) => "Extração de áudio: o vídeo original é preservado e somente a faixa de áudio é convertida.",
        MediaKind.Video when SelectedFormat?.Id.Equals("gif", StringComparison.OrdinalIgnoreCase) == true => "GIF de vídeo: personalize FPS, largura e cores nos ajustes. O original é preservado.",
        _ => "O arquivo original é preservado. Imagens com várias páginas usam a primeira página/quadro."
    };
    public bool IsBusy
    {
        get => _busy;
        private set
        {
            if (!Set(ref _busy, value)) return;
            Raise(nameof(IsIdle)); Raise(nameof(BusyVisibility)); Raise(nameof(CanRename));
            ChooseFileCommand.Notify(); ChooseDestinationCommand.Notify(); ResetDestinationCommand.Notify(); ConvertCommand.Notify(); CancelCommand.Notify(); ClearHistoryCommand.Notify();
            CheckUpdatesCommand.Notify(); UpdateActionCommand.Notify();
        }
    }
    public bool IsIdle => !IsBusy;
    public Visibility BusyVisibility => IsBusy ? Visibility.Visible : Visibility.Collapsed;
    public Visibility DetailsVisibility => _technicalDetails.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OutputVisibility => _lastOutput is null ? Visibility.Collapsed : Visibility.Visible;
    public Visibility HistoryEmptyVisibility => History.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public bool IsIndeterminate { get => _indeterminate; private set => Set(ref _indeterminate, value); }
    public double ProgressValue { get => _progress; private set => Set(ref _progress, value); }
    public AsyncCommand ChooseFileCommand { get; }
    public AsyncCommand ChooseDestinationCommand { get; }
    public AsyncCommand ResetDestinationCommand { get; }
    public AsyncCommand ConvertCommand { get; }
    public AsyncCommand CancelCommand { get; }
    public AsyncCommand CheckDependenciesCommand { get; }
    public AsyncCommand DetailsCommand { get; }
    public AsyncCommand OpenOutputCommand { get; }
    public AsyncCommand ClearHistoryCommand { get; }

    public Task InitializeAsync() => _initialization ??= InitializeCoreAsync();
    private async Task InitializeCoreAsync()
    {
        try
        {
            await Settings.LoadAsync();
            Options.Apply(Settings.DefaultConversionOptions, onlyIfUntouched: true);
            await Task.WhenAll(RefreshHistoryAsync(), CheckDependenciesAsync(false),
                _logger.WriteAsync("startup", $"NITH Converter {AppInfo.VersionText}; x64"));
            if (Settings.CheckForUpdatesOnStartup)
            {
                _updateTask = CheckForUpdatesAsync(force: false);
                await _updateTask;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ReportError(ex); }
    }
    public async Task SelectFileAsync(string path)
    {
        if (IsBusy) return;
        var selectionVersion = ++_selectionVersion;
        try
        {
            var info = await Task.Run(() =>
            {
                if (!FormatCatalog.IsSupportedInput(path)) throw new NotSupportedException("Formato não suportado.");
                var file = new FileInfo(path);
                if (!file.Exists) throw new FileNotFoundException();
                return (file.FullName, file.Name, file.Extension, file.Length);
            }, _lifetime.Token);
            if (IsBusy || selectionVersion != _selectionVersion || _lifetime.IsCancellationRequested) return;
            _inputPath = info.FullName;
            OutputBaseName = Path.GetFileNameWithoutExtension(info.Name);
            Raise(nameof(CanRename)); Raise(nameof(OutputNameError)); Raise(nameof(OutputNameErrorVisibility));
            FileName = info.Name;
            FileDescription = $"{info.Extension.TrimStart('.').ToUpperInvariant()}  •  {FormatSize(info.Length)}";
            Formats = FormatCatalog.GetOutputFormats(path);
            SelectedFormat = Formats.FirstOrDefault();
            Options.Configure(FormatCatalog.GetInputKind(path), SelectedFormat?.Id);
            Raise(nameof(FilePath)); Raise(nameof(ConversionHint));
            Status = "Arquivo pronto";
            StatusDetail = "Escolha o formato de saída e clique em Converter.";
            Severity = InfoBarSeverity.Informational;
            SetDetails("");
            ConvertCommand.Notify();
        }
        catch (OperationCanceledException) { }
        catch (NotSupportedException) { Status = "Formato ainda não suportado"; StatusDetail = "Escolha uma imagem, documento, vídeo ou áudio compatível."; Severity = InfoBarSeverity.Warning; }
        catch (Exception ex) { ReportError(ex); }
    }
    private Task StartConversionAsync() => _conversionTask = ConvertCoreAsync();
    private async Task ConvertCoreAsync()
    {
        if (_inputPath is null || SelectedFormat is null) return;
        IsBusy = true;
        _lastOutput = null; Raise(nameof(OutputVisibility)); OpenOutputCommand.Notify();
        SetDetails("");
        IsIndeterminate = true; ProgressValue = 0;
        Status = "Convertendo…"; StatusDetail = "Você pode consultar o histórico ou alterar configurações."; Severity = InfoBarSeverity.Informational;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _conversionCancellation = cancellation;
        using var progress = new UiProgress(_dispatcher, value =>
        {
            IsIndeterminate = value.Percent is null;
            if (value.Percent is double percent) ProgressValue = percent;
            StatusDetail = value.Message;
        });
        try
        {
            var folder = _destination ?? (string.IsNullOrWhiteSpace(Settings.DefaultOutputDirectory) ? null : Settings.DefaultOutputDirectory);
            var request = new ConversionRequest(_inputPath, SelectedFormat.Id, folder, OutputBaseName: OutputBaseName, Options: Options.Snapshot());
            var result = await _converter.ConvertAsync(request, progress, cancellation.Token);
            if (result.Error == ConversionError.OutputExists)
            {
                if (!Settings.ConfirmOverwrite)
                {
                    Status = "O arquivo de destino já existe";
                    StatusDetail = "Escolha outra pasta ou ative a confirmação de substituição nas configurações.";
                    Severity = InfoBarSeverity.Warning;
                    return;
                }
                if (!await _window.ConfirmReplaceAsync(Path.GetFileName(_converter.GetOutputPath(request))))
                { Status = "Conversão não iniciada"; StatusDetail = "O arquivo existente foi preservado."; return; }
                cancellation.Token.ThrowIfCancellationRequested();
                result = await _converter.ConvertAsync(request with { Overwrite = true }, progress, cancellation.Token);
            }
            // Drop any queued progress before publishing the final state.
            progress.Dispose();
            Status = result.Success ? "Conversão concluída" : result.Canceled ? "Conversão cancelada" : "Não foi possível converter";
            StatusDetail = result.UserMessage;
            Severity = result.Success ? InfoBarSeverity.Success : result.Canceled ? InfoBarSeverity.Informational : InfoBarSeverity.Error;
            SetDetails(result.TechnicalDetails ?? "");
            if (result.Success)
            {
                _lastOutput = result.OutputPath;
                Raise(nameof(OutputVisibility)); OpenOutputCommand.Notify();
                await RefreshHistoryAsync();
                if (Settings.OpenFolderAfterConversion && _lastOutput is not null)
                    await _window.OpenFolderAsync(Path.GetDirectoryName(_lastOutput)!);
            }
        }
        catch (OperationCanceledException) { Status = "Conversão cancelada"; StatusDetail = "O arquivo original foi preservado."; }
        finally { progress.Dispose(); _conversionCancellation = null; IsBusy = false; }
    }
    private async Task CheckForUpdatesAsync(bool force)
    {
        if (!force && !Settings.CheckForUpdatesOnStartup) return;
        UpdateStatus = "Verificando atualizações…";
        UpdateDetail = $"Versão instalada: {AppInfo.VersionText}";
        SetUpdateBanner(force, actionVisible: false);
        try
        {
            UpdateRelease? release = await _updates.CheckAsync(AppInfo.Version, _lifetime.Token);
            if (release is null)
            {
                _pendingUpdate = null; _downloadedUpdate = null;
                UpdateStatus = "NITH Converter está atualizado";
                string latest = _updates.LastKnownLatestVersion is null
                    ? "nenhuma release estável publicada"
                    : FormatVersion(_updates.LastKnownLatestVersion);
                UpdateDetail = $"Instalada: {AppInfo.VersionText} · Mais recente no GitHub: {latest}.";
                SetUpdateBanner(force, actionVisible: false);
                return;
            }

            _pendingUpdate = release;
            UpdateStatus = $"Nova versão {FormatVersion(release.Version)} disponível";
            UpdateDetail = release.HasIntegrityVerification
                ? $"Instalada: {AppInfo.VersionText} · GitHub: {FormatVersion(release.Version)}. O instalador será validado por SHA-256."
                : $"Instalada: {AppInfo.VersionText} · GitHub: {FormatVersion(release.Version)}. Esta release antiga não publicou SHA-256; o download será feito diretamente do GitHub por HTTPS.";
            UpdateActionText = Settings.AutoDownloadUpdates ? "Baixando…" : "Baixar atualização";
            SetUpdateBanner(true, actionVisible: !Settings.AutoDownloadUpdates);
            if (Settings.AutoDownloadUpdates) await DownloadPendingUpdateAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (HttpRequestException ex)
        {
            UpdateStatus = "Não foi possível verificar atualizações";
            UpdateDetail = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.Forbidden => "O GitHub recusou temporariamente a consulta ou atingiu o limite público. A versão não foi considerada atualizada; tente novamente em alguns minutos.",
                System.Net.HttpStatusCode.TooManyRequests => "O GitHub limitou temporariamente as consultas. A versão não foi considerada atualizada; tente novamente em alguns minutos.",
                System.Net.HttpStatusCode.NotFound => "O repositório ou a fonte de atualização não foi encontrada no GitHub.",
                _ => ex.Message
            };
            SetUpdateBanner(force, actionVisible: false);
            await _logger.WriteAsync("update.check_failed", $"{ex.GetType().Name}; status={(int?)ex.StatusCode}; {ex.Message}");
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            UpdateStatus = "Falha ao preparar a atualização";
            UpdateDetail = ex.Message;
            SetUpdateBanner(true, actionVisible: _pendingUpdate is not null);
            if (_pendingUpdate is not null) UpdateActionText = "Tentar novamente";
            await _logger.WriteAsync("update.failed", ex.GetType().Name);
        }
    }

    private async Task DownloadPendingUpdateAsync()
    {
        if (_pendingUpdate is null) return;
        UpdateStatus = $"Baixando atualização {_pendingUpdate.Tag}…";
        UpdateDetail = _pendingUpdate.HasIntegrityVerification
            ? "O instalador será verificado por SHA-256 antes de ficar disponível."
            : "Baixando o instalador oficial diretamente do GitHub. Esta release não possui SHA-256 publicado.";
        UpdateProgress = 0;
        UpdateActionText = "Baixando…";
        SetUpdateBanner(true, actionVisible: false);
        try
        {
            var numericProgress = new Progress<double>(percent => _dispatcher.TryEnqueue(() => UpdateProgress = percent));
            _downloadedUpdate = await _updates.DownloadAsync(_pendingUpdate, numericProgress, _lifetime.Token);
            UpdateProgress = 100;
            UpdateStatus = $"Atualização {_pendingUpdate.Tag} pronta";
            string integrity = _downloadedUpdate.IntegrityVerified
                ? "SHA-256 confirmado. "
                : "Download concluído pelo GitHub. ";
            UpdateDetail = integrity + "Clique em Instalar agora. O Windows pedirá permissão de administrador; o instalador fechará e tentará reabrir o aplicativo.";
            UpdateActionText = "Instalar agora";
            SetUpdateBanner(true, actionVisible: true);
            await _logger.WriteAsync("update.downloaded", _pendingUpdate.Tag);
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or System.Security.Cryptography.CryptographicException)
        {
            _downloadedUpdate = null;
            UpdateStatus = "Não foi possível baixar a atualização";
            UpdateDetail = ex switch
            {
                InvalidDataException => ex.Message,
                HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.Forbidden => "O GitHub recusou temporariamente o download. Tente novamente em alguns minutos.",
                HttpRequestException http when http.StatusCode == System.Net.HttpStatusCode.TooManyRequests => "O GitHub limitou temporariamente os downloads. Tente novamente em alguns minutos.",
                _ => "O download não foi concluído. Tente novamente; sua conexão pode estar funcionando normalmente e o GitHub pode ter respondido com erro temporário."
            };
            UpdateActionText = "Tentar novamente";
            SetUpdateBanner(true, actionVisible: true);
            await _logger.WriteAsync("update.download_failed", $"{ex.GetType().Name}; {ex.Message}");
        }
    }

    private async Task UpdateActionAsync()
    {
        if (_downloadedUpdate is null)
        {
            if (_pendingUpdate is not null) await DownloadPendingUpdateAsync();
            return;
        }
        UpdateStatus = "Abrindo instalador…";
        UpdateDetail = "Confirme a solicitação do Windows para concluir a atualização.";
        SetUpdateBanner(true, actionVisible: false);
        bool launched = await _window.LaunchUpdateInstallerAsync(_downloadedUpdate.InstallerPath);
        if (!launched)
        {
            UpdateStatus = "Instalação adiada";
            UpdateDetail = "A atualização continua baixada. Clique em Instalar agora quando quiser tentar novamente.";
            UpdateActionText = "Instalar agora";
            SetUpdateBanner(true, actionVisible: true);
            return;
        }
        await _logger.WriteAsync("update.install_started", _downloadedUpdate.Release.Tag);
        UpdateStatus = "Instalando atualização…";
        UpdateDetail = "O instalador fechará o NITH Converter, substituirá os arquivos e tentará reabrir o aplicativo.";
    }

    private static string FormatVersion(Version version) =>
        $"{version.Major}.{version.Minor}.{Math.Max(0, version.Build)}";

    private void SetUpdateBanner(bool visible, bool actionVisible)
    {
        _updateBannerVisible = visible; _updateActionVisible = actionVisible;
        Raise(nameof(UpdateBannerVisibility)); Raise(nameof(UpdateActionVisibility));
        UpdateActionCommand.Notify();
    }

    private async Task CheckDependenciesAsync(bool force)
    {
        DependencyStatus = "Verificando mecanismos…";
        var result = await _dependencies.DiscoverAsync(force, _lifetime.Token);
        ImageMagickStatus = result.ImageMagickPath is null ? "Não encontrado · necessário para imagens" : "Disponível · imagens e criação de PDF";
        FFmpegStatus = result.FFmpegPath is null ? "Não encontrado · necessário para vídeos e áudio" : result.FFprobePath is null ? "Disponível · áudio/vídeo com progresso indeterminado (ffprobe ausente)" : "Disponível · áudio, vídeo e progresso real";
        PdfStatus = result.GhostscriptPath is null ? "Ghostscript ausente · PDF/PS/EPS indisponíveis" : "Ghostscript disponível · leitura de PDF/PS/EPS";
        DependencyStatus = result.ImageMagickPath is not null && result.FFmpegPath is not null ? "Mecanismos disponíveis" : "Verifique os mecanismos nas configurações";
        await _logger.WriteAsync("dependencies", $"imagemagick={result.ImageMagickPath is not null}; ffmpeg={result.FFmpegPath is not null}; ffprobe={result.FFprobePath is not null}");
    }
    private async Task RefreshHistoryAsync()
    {
        var entries = await _history.LoadAsync(_lifetime.Token);
        History.Clear();
        foreach (var entry in entries) History.Add(entry);
        Raise(nameof(HistoryEmptyVisibility));
    }
    private void SettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    { if (e.PropertyName == nameof(Settings.DefaultOutputDirectory)) Raise(nameof(DestinationLabel)); }
    private void SetDetails(string text) { _technicalDetails = text; Raise(nameof(DetailsVisibility)); }
    public void ReportError(Exception exception)
    {
        Status = "Não foi possível concluir a operação";
        StatusDetail = "Verifique o arquivo, a pasta de destino e as permissões de acesso.";
        Severity = InfoBarSeverity.Error;
        SetDetails(exception.ToString());
    }
    public async Task CancelAsync()
    {
        StatusDetail = "Cancelando e encerrando os processos…";
        if (_conversionCancellation is { } cancellation) await cancellation.CancelAsync();
    }
    public async Task ShutdownAsync()
    {
        await _lifetime.CancelAsync();
        try
        {
            await Task.WhenAll(_conversionTask ?? Task.CompletedTask, _initialization ?? Task.CompletedTask,
                ChooseFileCommand.ExecutionTask, ChooseDestinationCommand.ExecutionTask, ConvertCommand.ExecutionTask,
                CancelCommand.ExecutionTask, CheckDependenciesCommand.ExecutionTask, DetailsCommand.ExecutionTask,
                OpenOutputCommand.ExecutionTask, ClearHistoryCommand.ExecutionTask,
                Settings.SaveCommand.ExecutionTask, Settings.BrowseCommand.ExecutionTask,
                SaveOptionsCommand.ExecutionTask, ResetOptionsCommand.ExecutionTask,
                CheckUpdatesCommand.ExecutionTask, UpdateActionCommand.ExecutionTask, _updateTask ?? Task.CompletedTask);
        }
        catch (OperationCanceledException) { }
        catch (Exception) { /* Operations expose their failures through commands; shutdown still releases resources. */ }
        await _logger.WriteAsync("shutdown");
    }
    public void Dispose() { Settings.PropertyChanged -= SettingsChanged; _lifetime.Dispose(); }
    private static string FormatSize(long bytes) => bytes >= 1024L * 1024 * 1024 ? $"{bytes / (1024d * 1024 * 1024):0.##} GB" : bytes >= 1024 * 1024 ? $"{bytes / (1024d * 1024):0.##} MB" : $"{Math.Max(1, bytes / 1024d):0.#} KB";
}
