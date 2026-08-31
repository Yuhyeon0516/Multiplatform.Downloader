using System.Windows;

namespace Multiplatform_Downloader.Views;

public partial class SettingsView : Window
{
    public SettingsView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Multiplatform_Downloader.Services.ThemeService.ApplyTitleBarTo(this);
    }
}
