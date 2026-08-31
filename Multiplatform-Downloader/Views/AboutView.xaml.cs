using System.Windows;

namespace Multiplatform_Downloader.Views;

public partial class AboutView : Window
{
    public AboutView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Multiplatform_Downloader.Services.ThemeService.ApplyTitleBarTo(this);
    }
}
