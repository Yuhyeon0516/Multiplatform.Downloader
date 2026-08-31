using System.Windows;

namespace Multiplatform_Downloader.Views;

public partial class AddLinksView : Window
{
    public AddLinksView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Multiplatform_Downloader.Services.ThemeService.ApplyTitleBarTo(this);
    }
}
