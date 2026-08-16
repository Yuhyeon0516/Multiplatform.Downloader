using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Multiplatform_Downloader.ViewModels;

namespace Multiplatform_Downloader.Views;

/// <summary>
/// ShellView.xaml 코드비하인드 — 드래그&드롭 등록(FR-U2.5)만 담당(뷰 전용 로직).
/// </summary>
public partial class ShellView : Window
{
    public ShellView()
    {
        InitializeComponent();
    }

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        e.Effects = HasDroppableText(e.Data) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;
        var text = ExtractText(e.Data);
        if (!string.IsNullOrWhiteSpace(text))
            shell.EnqueueText(text);
        e.Handled = true;
    }

    /// <summary>완료 카드 더블클릭 = 재생(§9). 재생 불가 상태면 무시.</summary>
    private void OnCardMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2
            && sender is FrameworkElement { DataContext: DownloadItemViewModel { CanPlayItem: true } card })
        {
            card.PlayItem();
            e.Handled = true;
        }
    }

    /// <summary>오버플로 메뉴 항목 클릭 시 팝업을 닫는다(뷰 전용 로직 — 액션은 Caliburn Attach가 수행).</summary>
    private void OnOverflowActionClick(object sender, RoutedEventArgs e)
    {
        var node = sender as DependencyObject;
        while (node is not null)
        {
            if (node is Popup popup)
            {
                popup.IsOpen = false;
                return;
            }
            node = node is FrameworkElement { Parent: not null } fe ? fe.Parent : VisualTreeHelper.GetParent(node);
        }
    }

    private static bool HasDroppableText(IDataObject data) =>
        data.GetDataPresent(DataFormats.UnicodeText) || data.GetDataPresent(DataFormats.Text);

    private static string? ExtractText(IDataObject data)
    {
        if (data.GetDataPresent(DataFormats.UnicodeText))
            return data.GetData(DataFormats.UnicodeText) as string;
        if (data.GetDataPresent(DataFormats.Text))
            return data.GetData(DataFormats.Text) as string;
        return null;
    }
}
