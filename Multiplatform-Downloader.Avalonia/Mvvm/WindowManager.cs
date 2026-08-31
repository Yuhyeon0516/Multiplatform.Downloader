using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;

namespace Multiplatform_Downloader.Avalonia.Mvvm;

/// <summary>
/// 이름 규약(ViewModels.XxxViewModel → Views.XxxView)으로 창을 생성·표시한다.
/// Screen.TryCloseAsync ↔ Window.Close, CanCloseAsync ↔ Window.Closing을 배선한다.
/// </summary>
public sealed class WindowManager : IWindowManager
{
    public async Task<bool?> ShowDialogAsync(object viewModel)
    {
        var window = CreateWindow(viewModel);
        var owner = MainWindow;
        if (owner is null || !owner.IsVisible)
        {
            // 소유자가 없으면(트레이 상주 등) 독립 창으로 띄우고 닫힘을 기다린다
            var closed = new TaskCompletionSource<bool?>();
            window.Closed += (_, _) => closed.TrySetResult(window.Tag as bool?);
            window.Show();
            return await closed.Task;
        }
        return await window.ShowDialog<bool?>(owner);
    }

    public Task ShowWindowAsync(object viewModel)
    {
        CreateWindow(viewModel).Show();
        return Task.CompletedTask;
    }

    private static Window? MainWindow =>
        (global::Avalonia.Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    private static Window CreateWindow(object viewModel)
    {
        var vmType = viewModel.GetType();
        var viewTypeName = vmType.FullName!
            .Replace(".ViewModels.", ".Views.")
            .Replace("ViewModel", "View");
        var viewType = vmType.Assembly.GetType(viewTypeName)
            ?? throw new InvalidOperationException($"뷰를 찾을 수 없습니다: {viewTypeName}");
        var window = (Window)Activator.CreateInstance(viewType)!;
        window.DataContext = viewModel;

        if (viewModel is Screen screen)
        {
            // XAML에서 Title을 바인딩한 창(플레이어 등)은 덮어쓰지 않는다 — 바인딩이 끊긴다
            if (string.IsNullOrEmpty(window.Title) && !string.IsNullOrEmpty(screen.DisplayName))
                window.Title = screen.DisplayName;

            var closeApproved = false;
            screen.CloseRequested += result =>
            {
                closeApproved = true; // VM 주도 닫기 — CanCloseAsync는 이미 VM이 판단한 것으로 본다
                window.Tag = result;
                window.Close(result);
            };
            window.Closing += async (_, e) =>
            {
                if (closeApproved)
                    return;
                e.Cancel = true; // 먼저 취소해 두고 비동기 판정 후 재닫기
                if (await screen.CanCloseAsync())
                {
                    closeApproved = true;
                    window.Close(window.Tag as bool?);
                }
            };
        }
        return window;
    }
}
