namespace NithConverter.Services;

public interface IWindowInteraction
{
    Task<string?> PickFileAsync();
    Task<string?> PickFolderAsync();
    Task<bool> ConfirmReplaceAsync(string fileName);
    Task ShowDetailsAsync(string details);
    Task OpenFolderAsync(string path);
    Task<bool> LaunchUpdateInstallerAsync(string installerPath);
}
