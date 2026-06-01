using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HyperVMManager.Dialogs;
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
        Title = AppBrand.DisplayName;
        TxtBrandName.Text = AppBrand.DisplayName;
        TxtAppVersion.Text = "v" + GetCurrentAppVersion();

        _ = CheckForUpdatesAsync(silentIfNoUpdate: true);
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = AppUserSettings.Load();
        if (!settings.HasSeenFirstRunTips)
        {
            TutorialDialog.ShowFor(this, markSeen: true);
        }
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

            MessageBox.Show("The update installer is ready. The app will close first, then setup will start automatically.", "Update", MessageBoxButton.OK, MessageBoxImage.Information);
            LaunchInstallerAfterExit(installerPath);
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

    private static void LaunchInstallerAfterExit(string installerPath)
    {
        if (string.IsNullOrWhiteSpace(installerPath))
        {
            throw new InvalidOperationException("Installer path is invalid.");
        }

        // Create a batch file that waits for app exit, then runs installer
        // Batch files launched via cmd.exe /c run independently of the parent process
        var scriptDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "HyperVMManager", "updates");
        System.IO.Directory.CreateDirectory(scriptDir);
        var batPath = System.IO.Path.Combine(scriptDir, "update.bat");
        var logPath = System.IO.Path.Combine(scriptDir, "update_log.txt");

        // Build batch script — use %TEMP% to avoid hardcoding paths
        var batLines = new[]
        {
            "@echo off",
            $"echo {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Update script started > \"{logPath}\"",
            $"echo Installer: \"{installerPath}\" >> \"{logPath}\"",
            "",
            "REM Wait for app to exit (max 30 seconds)",
            "set /a count=0",
            ":waitloop",
            "tasklist /FI \"IMAGENAME eq HyperVMManager.exe\" 2>NUL | find /I \"HyperVMManager.exe\" >NUL",
            "if errorlevel 1 (",
            $"    echo {DateTime.Now:yyyy-MM-dd HH:mm:ss} - App exited >> \"{logPath}\"",
            "    goto :runinstaller",
            ")",
            "if %count% GEQ 30 (",
            $"    echo {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Timeout >> \"{logPath}\"",
            "    goto :runinstaller",
            ")",
            "set /a count+=1",
            "timeout /t 1 /nobreak >NUL",
            "goto :waitloop",
            "",
            ":runinstaller",
            $"echo {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Starting installer... >> \"{logPath}\"",
            $"start \"\" /WAIT \"{installerPath}\"",
            $"echo {DateTime.Now:yyyy-MM-dd HH:mm:ss} - Installer finished >> \"{logPath}\"",
            "del \"%~f0\""
        };

        var batScript = string.Join(Environment.NewLine, batLines);
        System.IO.File.WriteAllText(batPath, batScript);

        // Launch cmd.exe with start command — runs independently of the app
        Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{batPath}\"",
            UseShellExecute = true,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private void BtnHelp_Click(object sender, RoutedEventArgs e)
    {
        TutorialDialog.ShowFor(this, markSeen: false);
    }

    private void VmDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VmDataGrid.SelectedItem is VirtualMachine vm)
            _viewModel.OnVmRowSelected(vm);
        else
            _viewModel.OnVmRowSelected(null);
    }
}
