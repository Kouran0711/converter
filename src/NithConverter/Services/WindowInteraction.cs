using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NithConverter.Core.Models;
using Windows.Storage.Pickers;

namespace NithConverter.Services;

public sealed class WindowInteraction(Window window) : IWindowInteraction
{
    private readonly SemaphoreSlim _dialogGate = new(1, 1);
    private ContentDialog? _activeDialog;
    private bool _closing;
    public void CloseDialogs() { _closing = true; _activeDialog?.Hide(); }
    private nint Handle => WinRT.Interop.WindowNative.GetWindowHandle(window);
    public async Task<string?> PickFileAsync()
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary, ViewMode = PickerViewMode.Thumbnail };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        foreach (var extension in FormatCatalog.SupportedExtensions) picker.FileTypeFilter.Add(extension);
        return (await picker.PickSingleFileAsync())?.Path;
    }
    public async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Handle);
        picker.FileTypeFilter.Add("*");
        return (await picker.PickSingleFolderAsync())?.Path;
    }
    public async Task<bool> ConfirmReplaceAsync(string fileName)
    {
        await _dialogGate.WaitAsync();
        try
        {
            if (_closing) return false;
            _activeDialog = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
                Title = "Esse arquivo já existe",
                Content = $"{fileName}\n\nDeseja substituir o arquivo de destino? O original será preservado.",
                PrimaryButtonText = "Substituir", CloseButtonText = "Cancelar", DefaultButton = ContentDialogButton.Close
            };
            return await _activeDialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally { _activeDialog = null; _dialogGate.Release(); }
    }
    public async Task ShowDetailsAsync(string details)
    {
        await _dialogGate.WaitAsync();
        try
        {
            if (_closing) return;
            _activeDialog = new ContentDialog
            {
                XamlRoot = window.Content.XamlRoot, RequestedTheme = ElementTheme.Dark,
                Title = "Detalhes técnicos", CloseButtonText = "Fechar",
                Content = new ScrollViewer { MaxHeight = 360, Content = new TextBox { Text = details, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") } }
            };
            await _activeDialog.ShowAsync();
        }
        finally { _activeDialog = null; _dialogGate.Release(); }
    }
    public Task OpenFolderAsync(string path) => Task.Run(() =>
    {
        if (!Directory.Exists(path)) throw new DirectoryNotFoundException();
        using var process = Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
    });

    public Task<bool> LaunchUpdateInstallerAsync(string installerPath) => Task.Run(() =>
    {
        if (!File.Exists(installerPath)) throw new FileNotFoundException("O instalador da atualização não foi encontrado.", installerPath);
        try
        {
            using Process? process = Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                Arguments = "/SP- /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS",
                WorkingDirectory = Path.GetDirectoryName(installerPath)!,
                UseShellExecute = true,
                Verb = "runas"
            });
            return process is not null;
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false; // UAC cancelado pelo usuário.
        }
    });

}

