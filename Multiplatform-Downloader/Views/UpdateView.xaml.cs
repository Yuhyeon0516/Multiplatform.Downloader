using System.Windows;

namespace Multiplatform_Downloader.Views;

public partial class UpdateView : Window
{
    public UpdateView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Multiplatform_Downloader.Services.ThemeService.ApplyTitleBarTo(this);
    }
}
