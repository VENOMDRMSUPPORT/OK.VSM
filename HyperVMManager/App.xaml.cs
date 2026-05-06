using System.Windows;

namespace HyperVMManager;

public partial class App : Application
{
    private void App_OnStartup(object sender, StartupEventArgs e)
    {
        var window = new MainWindow();
        window.Show();
    }
}
