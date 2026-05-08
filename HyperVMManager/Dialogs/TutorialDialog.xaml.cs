using System.Windows;
using System.Windows.Input;
using HyperVMManager.Services;

namespace HyperVMManager.Dialogs;

public partial class TutorialDialog : Window
{
    private readonly bool _markSeen;

    public TutorialDialog(bool markSeen)
    {
        InitializeComponent();
        _markSeen = markSeen;
        Title = AppBrand.DisplayName + " Tips";
    }

    public static void ShowFor(Window? owner, bool markSeen)
    {
        TutorialDialog dialog = new TutorialDialog(markSeen);
        if (owner != null)
        {
            dialog.Owner = owner;
        }
        dialog.ShowDialog();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }

    protected override void OnClosed(System.EventArgs e)
    {
        base.OnClosed(e);
        if (_markSeen)
        {
            var settings = AppUserSettings.Load();
            settings.HasSeenFirstRunTips = true;
            settings.Save();
        }
    }

    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }
}
