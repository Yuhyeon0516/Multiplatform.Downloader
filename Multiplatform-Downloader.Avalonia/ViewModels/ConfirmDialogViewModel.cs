using Multiplatform_Downloader.Avalonia.Mvvm;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>테마 일치 확인 대화상자 — WPF 헤드 이식(무수정).</summary>
public sealed class ConfirmDialogViewModel : Screen
{
    public ConfirmDialogViewModel(string title, string message, string confirmText = "삭제", string cancelText = "취소")
    {
        DisplayName = title;
        Title = title;
        Message = message;
        ConfirmText = confirmText;
        CancelText = cancelText;
    }

    public string Title { get; }
    public string Message { get; }
    public string ConfirmText { get; }
    public string CancelText { get; }

    public bool Confirmed { get; private set; }

    public Task Confirm()
    {
        Confirmed = true;
        return TryCloseAsync(true);
    }

    public Task Cancel() => TryCloseAsync(false);
}
