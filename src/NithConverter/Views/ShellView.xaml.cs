using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using NithConverter.ViewModels;
using Windows.ApplicationModel.DataTransfer;

namespace NithConverter.Views;

public sealed partial class ShellView : UserControl
{
    public MainViewModel ViewModel { get; }
    public UIElement DragTitleBar => TitleBar;
    public ShellView(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
        // NavigationView's stock settings label follows the OS language; the app UI is Portuguese.
        if (Navigation.SettingsItem is NavigationViewItem settings) settings.Content = "Configurações";
    }
    private void OnNavigationChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (ConverterPage is null) return;
        var tag = (args.SelectedItemContainer as NavigationViewItem)?.Tag as string;
        ConverterPage.Visibility = !args.IsSettingsSelected && tag != "history" ? Visibility.Visible : Visibility.Collapsed;
        HistoryPage.Visibility = tag == "history" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = args.IsSettingsSelected ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnPaneClosing(NavigationView sender, NavigationViewPaneClosingEventArgs args) => args.Cancel = true;
    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = !ViewModel.IsBusy && e.DataView.Contains(StandardDataFormats.StorageItems) ? DataPackageOperation.Copy : DataPackageOperation.None;
        e.DragUIOverride.Caption = "Converter arquivo";
        e.Handled = true;
    }
    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (ViewModel.IsBusy || !e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var deferral = e.GetDeferral();
        try
        {
            var items = await e.DataView.GetStorageItemsAsync();
            if (items.Count != 1 || items[0] is not Windows.Storage.StorageFile file)
            { await new ContentDialog { XamlRoot = XamlRoot, Title = "Selecione um arquivo", Content = "Arraste um único arquivo por vez.", CloseButtonText = "Entendi" }.ShowAsync(); return; }
            await ViewModel.SelectFileAsync(file.Path);
            Navigation.SelectedItem = ConvertItem;
        }
        catch (Exception ex) { ViewModel.ReportError(ex); }
        finally { deferral.Complete(); }
    }
}
