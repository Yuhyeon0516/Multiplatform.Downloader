namespace Multiplatform_Downloader.Avalonia.Mvvm;

/// <summary>
/// Caliburn.Micro Screen 호환 최소 구현 — DisplayName·TryCloseAsync·CanCloseAsync만 제공한다.
/// WindowManager가 CloseRequested를 구독해 해당 창을 닫는다.
/// </summary>
public abstract class Screen : PropertyChangedBase
{
    private string _displayName = string.Empty;

    /// <summary>창 제목으로 사용된다.</summary>
    public string DisplayName
    {
        get => _displayName;
        set { _displayName = value; NotifyOfPropertyChange(); }
    }

    /// <summary>VM이 자기 창 닫기를 요청 — WindowManager가 배선한다.</summary>
    public event Action<bool?>? CloseRequested;

    public Task TryCloseAsync(bool? dialogResult = null)
    {
        CloseRequested?.Invoke(dialogResult);
        return Task.CompletedTask;
    }

    /// <summary>창이 닫히기 전 호출된다(X 버튼 포함). false 반환 시 닫기 취소.</summary>
    public virtual Task<bool> CanCloseAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);
}
