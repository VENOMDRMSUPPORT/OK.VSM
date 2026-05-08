using System.Windows;
using System.Windows.Threading;

namespace HyperVMManager;

public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        DispatcherUnhandledException += App_DispatcherUnhandledException;
        var window = new MainWindow();
        window.Show();
    }

    private static void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show("Unexpected error:\n" + e.Exception.Message, "VENOM VM-WARE", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
