using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperVMManager.Models;
using HyperVMManager.Services;
using HyperVMManager.ViewModels;

namespace HyperVMManager;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private const string DefaultUpdateManifestUrl = "https://github.com/VENOMDRMSUPPORT/OK.VSM/releases/latest/download/latest.json";
    private bool _isUpdateCheckInProgress;

    public MainWindow()
    {
        InitializeComponent();
        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.VirtualMachines.CollectionChanged += VirtualMachines_CollectionChanged;
        UpdateStats();
        TxtAppVersion.Text = "v" + GetCurrentAppVersion();

        _ = CheckForUpdatesAsync(silentIfNoUpdate: true);
    }

    private static string GetCurrentAppVersion()
    {
        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v == null ? "1.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private async Task CheckForUpdatesAsync(bool silentIfNoUpdate)
    {
        if (_isUpdateCheckInProgress)
        {
            return;
        }

        _isUpdateCheckInProgress = true;
        BtnCheckUpdates.IsEnabled = false;

        try
        {
            if (!Uri.TryCreate(DefaultUpdateManifestUrl, UriKind.Absolute, out var manifestUri))
            {
                if (!silentIfNoUpdate)
                {
                    MessageBox.Show("Invalid update manifest URL.", "Update", MessageBoxButton.OK, MessageBoxImage.Exclamation);
                }
                return;
            }

            var currentVersion = Version.Parse(GetCurrentAppVersion());
            var result = await AppUpdateService.CheckForUpdateAsync(manifestUri, currentVersion);

            if (!result.IsUpdateAvailable || result.Manifest == null)
            {
                if (!silentIfNoUpdate)
                {
                    MessageBox.Show("You already have the latest version.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                return;
            }

            var prompt =
                $"A new version is available: {result.Manifest.Version}\n\n" +
                $"Current version: {currentVersion}\n\n" +
                "Do you want to download and install it now?";
            if (MessageBox.Show(prompt, "Update Available", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                return;
            }

            var downloadDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HyperVMManager", "updates");
            var installerPath = await AppUpdateService.DownloadUpdateAsync(result.Manifest, downloadDir);

            MessageBox.Show("The update installer is ready and will start now.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);

            Process.Start(new ProcessStartInfo
            {
                FileName = installerPath,
                UseShellExecute = true
            });

            Application.Current.Shutdown();
        }
        catch (Exception ex)
        {
            if (!silentIfNoUpdate)
            {
                MessageBox.Show("Update failed:\n" + ex.Message, "Update", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }
        finally
        {
            _isUpdateCheckInProgress = false;
            BtnCheckUpdates.IsEnabled = true;
        }
    }

    private void ViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsLoading) && !_viewModel.IsLoading)
        {
            UpdateStats();
            UpdateVisibility();
        }

        if (e.PropertyName is nameof(MainViewModel.RunningCount) or nameof(MainViewModel.StoppedCount)
            or nameof(MainViewModel.OtherCount))
            UpdateStats();

        if (e.PropertyName == nameof(MainViewModel.SelectedVm) && _viewModel.SelectedVm == null)
            VmDataGrid.SelectedItem = null;
    }

    private void VirtualMachines_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdateStats();
            UpdateVisibility();
        });
    }

    private void UpdateVisibility()
    {
        Dispatcher.Invoke(() =>
        {
            var hasVMs = _viewModel.VirtualMachines.Count > 0;
            EmptyState.Visibility = hasVMs || _viewModel.IsLoading ? Visibility.Collapsed : Visibility.Visible;
            VmDataGrid.Visibility = hasVMs ? Visibility.Visible : Visibility.Collapsed;
        });
    }

    private void UpdateStats()
    {
        Dispatcher.Invoke(() =>
        {
            TxtTotal.Text = _viewModel.VirtualMachines.Count.ToString();
            TxtRunning.Text = _viewModel.RunningCount.ToString();
            TxtStopped.Text = _viewModel.StoppedCount.ToString();
            TxtOther.Text = _viewModel.OtherCount.ToString();
        });
    }

    private void VmDataGrid_LoadingRow(object sender, DataGridRowEventArgs e)
    {
        e.Row.Header = (e.Row.GetIndex() + 1).ToString();
    }

    private void TitleBar_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
            DragMove();
    }

    private void BtnMinimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void BtnMaximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

    private void BtnRefresh_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RefreshCommand.Execute(null);
    }

    private void BtnPool_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenNetworkPoolSettingsCommand.Execute(null);
    }

    private void BtnCreateVm_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenCreateUbuntuVmCommand.Execute(null);
    }

    private async void BtnCheckUpdates_Click(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(silentIfNoUpdate: false);
    }

    private void VmDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VmDataGrid.SelectedItem is VirtualMachine vm)
            _viewModel.OnVmRowSelected(vm);
        else
            _viewModel.OnVmRowSelected(null);
    }
}
