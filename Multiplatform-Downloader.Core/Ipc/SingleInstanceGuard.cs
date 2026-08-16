namespace Multiplatform_Downloader.Core.Ipc;

/// <summary>
/// 이름 있는 <see cref="Mutex"/>로 단일 인스턴스를 보장한다(FR-08).
/// 두 번째 인스턴스는 <see cref="IsPrimaryInstance"/>가 false이며, URL만 IPC로 넘기고 종료해야 한다.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private bool _owned;

    public SingleInstanceGuard(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _mutex = new Mutex(initiallyOwned: true, name, out var createdNew);
        IsPrimaryInstance = createdNew;
        _owned = createdNew;
    }

    public bool IsPrimaryInstance { get; }

    public void Dispose()
    {
        if (_owned)
        {
            try { _mutex.ReleaseMutex(); } catch { /* 소유 아님 — 무시 */ }
            _owned = false;
        }
        _mutex.Dispose();
    }
}
