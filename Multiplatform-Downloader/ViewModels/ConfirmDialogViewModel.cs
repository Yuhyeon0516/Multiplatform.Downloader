using Caliburn.Micro;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>
/// 디자인 컨셉(와이어프레임 토큰)을 따르는 확인 대화상자. 시스템 MessageBox 대체 —
/// 다크/라이트 테마의 Wf* DynamicResource 를 그대로 사용한다.
/// </summary>
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

    /// <summary>[확인] 선택 여부 — 닫힌 뒤 호출측이 판정.</summary>
    public bool Confirmed { get; private set; }

    public Task Confirm()
    {
        Confirmed = true;
        return TryCloseAsync(true);
    }

    public Task Cancel() => TryCloseAsync(false);
}
