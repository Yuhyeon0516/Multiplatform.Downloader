namespace Multiplatform_Downloader.Core.Ipc;

/// <summary>
/// 이름 있는 <see cref="Mutex"/>로 단일 인스턴스를 보장한다(FR-08).
/// 두 번째 인스턴스는 <see cref="IsPrimaryInstance"/>가 false이며, URL만 IPC로 넘기고 종료해야 한다.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex? _mutex;
    private bool _owned;

    public SingleInstanceGuard(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        try
        {
            _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
            IsPrimaryInstance = createdNew;
            _owned = createdNew;
        }
        catch (Exception)
        {
            // 이름이 고정 상수이므로 Mutex 생성자가 던지는 실질적 원인은 '같은 이름의 뮤텍스가 접근 불가'
            // (다른 무결성 레벨—예: 관리자 권한—인스턴스가 소유)한 경우다. 즉 예외 자체가 '다른 인스턴스가
            // 이미 존재한다'는 신호다. 이를 흘리면 async void OnStartup에서 앱이 크래시하므로, 어떤 예외든
            // 일관되게 2차 인스턴스로 취급한다(전달 시도 후 종료). 새 뮤텍스를 만들지 않아 중복 주 창을 피한다.
            // (근본 해결은 앱의 일반 권한 자기 재실행 — StartupGuards.TryRelaunchUnelevated)
            _mutex = null;
            IsPrimaryInstance = false;
            _owned = false;
        }
    }

    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (_owned && _mutex is not null)
        {
            try { _mutex.ReleaseMutex(); } catch { /* 소유 아님 — 무시 */ }
            _owned = false;
        }
        _mutex?.Dispose();
    }
}
