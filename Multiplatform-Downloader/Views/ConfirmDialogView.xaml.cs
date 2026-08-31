using System.Windows;

namespace Multiplatform_Downloader.Views;

public partial class ConfirmDialogView : Window
{
    public ConfirmDialogView()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => Multiplatform_Downloader.Services.ThemeService.ApplyTitleBarTo(this);
    }
}
