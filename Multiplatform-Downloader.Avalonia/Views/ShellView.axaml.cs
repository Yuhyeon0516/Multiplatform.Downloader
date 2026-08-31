using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Multiplatform_Downloader.Avalonia.ViewModels;

namespace Multiplatform_Downloader.Avalonia.Views;

/// <summary>ShellView 코드비하인드 — 드래그&드롭 등록(FR-U2.5)·더블클릭 재생·오버플로 닫기(뷰 전용 로직).</summary>
public partial class ShellView : Window
{
    public ShellView()
    {
        InitializeComponent();
        AddHandler(DragDrop.DragOverEvent, OnWindowDragOver);
        AddHandler(DragDrop.DropEvent, OnWindowDrop);
    }

    private void OnWindowDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.Data.Contains(DataFormats.Text) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnWindowDrop(object? sender, DragEventArgs e)
    {
        if (DataContext is not ShellViewModel shell)
            return;
        var text = e.Data.GetText();
        if (!string.IsNullOrWhiteSpace(text))
            shell.EnqueueText(text);
        e.Handled = true;
    }

    /// <summary>완료 카드 더블클릭 = 재생(§9).</summary>
    private void OnCardDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { DataContext: DownloadItemViewModel { CanPlayItem: true } card })
        {
            card.PlayItem();
            e.Handled = true;
        }
    }

    /// <summary>오버플로 메뉴 항목 클릭 시 플라이아웃을 닫는다(액션은 Command가 수행).
    /// 주의: Button은 Click 이벤트 → Command 순서로 실행하므로, 여기서 동기로 닫으면
    /// 버튼이 트리에서 분리되며 Command 바인딩이 해제돼 액션이 실행되지 않는다(실측) —
    /// 닫기를 디스패처로 미뤄 커맨드 실행 이후에 닫는다.</summary>
    private void OnOverflowActionClick(object? sender, RoutedEventArgs e)
    {
        var popup = (sender as Control)?.FindAncestorOfType<Popup>();
        if (popup is not null)
            global::Avalonia.Threading.Dispatcher.UIThread.Post(popup.Close);
    }
}
