using System.Linq;
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

    /// <summary>완료 카드 더블클릭 = 재생(§9). 단일 클릭은 드래그 아웃 시작점 기록(FR-DG1) —
    /// 버튼·체크박스·콤보 클릭은 해당 컨트롤이 Handled 처리해 여기 오지 않는다(FR-DG4).</summary>
    private void OnCardMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2
            && sender is FrameworkElement { DataContext: DownloadItemViewModel { CanPlayItem: true } card })
        {
            card.PlayItem();
            e.Handled = true;
            return;
        }
        if (sender is FrameworkElement { DataContext: DownloadItemViewModel { CanDragItem: true } } source)
        {
            _dragStart = e.GetPosition(null);
            _dragSourceCard = source;
        }
    }

    private System.Windows.Point _dragStart;
    private FrameworkElement? _dragSourceCard;
    private bool _isDragging;

    /// <summary>임계값(시스템 설정) 초과 시 완료 파일을 외부 앱으로 드래그(FR-DG1~DG3).
    /// DoDragDrop은 블로킹(모달 루프) — UI 스레드 동기 호출이 WPF 표준.</summary>
    private void OnCardMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_isDragging || _dragSourceCard is null || !ReferenceEquals(sender, _dragSourceCard)
            || e.LeftButton != System.Windows.Input.MouseButtonState.Pressed)
            return;

        var pos = e.GetPosition(null);
        if (System.Math.Abs(pos.X - _dragStart.X) <= SystemParameters.MinimumHorizontalDragDistance
            && System.Math.Abs(pos.Y - _dragStart.Y) <= SystemParameters.MinimumVerticalDragDistance)
            return;

        if (DataContext is not ShellViewModel shell
            || _dragSourceCard.DataContext is not DownloadItemViewModel { CanDragItem: true } card)
            return;

        var paths = shell.CollectDragPaths(card);
        if (paths.Count == 0)
        {
            _dragSourceCard = null; // 실파일 없음 — 이 제스처에서는 재시도하지 않는다
            return;
        }

        _isDragging = true;
        try
        {
            // FileDrop 단독(텍스트 포맷 미포함 — 자기 창 URL 드롭존 재등록 차단, FR-DG1)
            // Copy 단독(Move 허용 시 같은 볼륨 드롭에서 원본이 이동·소실될 위험)
            var data = new DataObject(DataFormats.FileDrop, paths.ToArray());
            DragDrop.DoDragDrop(_dragSourceCard, data, DragDropEffects.Copy);
        }
        finally
        {
            _isDragging = false;
            _dragSourceCard = null;
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
