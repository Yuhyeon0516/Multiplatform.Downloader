using Multiplatform_Downloader.Core.Abstractions;

namespace Multiplatform_Downloader.Core.Diagnostics;

/// <summary>
/// 메모리(최근 N개) + 파일에 기록하고 <see cref="Logged"/> 이벤트로 실시간 전달하는 로거.
/// 자격증명·토큰이 메시지에 들어오지 않도록 호출부가 마스킹 책임을 진다(NFR-13).
/// </summary>
public sealed class AppLogger : IAppLogger
{
    private const int MaxRecent = 1000;

    private readonly IClock _clock;
    private readonly string? _logFilePath;
    private readonly object _gate = new();
    private readonly LinkedList<LogEntry> _recent = new();

    public AppLogger(IClock clock, string? logFilePath = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logFilePath = logFilePath;
        if (!string.IsNullOrEmpty(_logFilePath))
        {
            var dir = Path.GetDirectoryName(_logFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
        }
    }

    public event EventHandler<LogEntry>? Logged;

    public IReadOnlyList<LogEntry> Recent
    {
        get { lock (_gate) return _recent.ToArray(); }
    }

    public void Log(AppLogLevel level, string category, string message)
    {
        var entry = new LogEntry(_clock.Now, level, category, message);

        lock (_gate)
        {
            _recent.AddLast(entry);
            while (_recent.Count > MaxRecent)
                _recent.RemoveFirst();
        }

        AppendToFile(entry);
        Logged?.Invoke(this, entry); // 이벤트는 lock 밖(I-05)
    }

    private void AppendToFile(LogEntry entry)
    {
        if (string.IsNullOrEmpty(_logFilePath))
            return;
        try
        {
            lock (_gate)
                File.AppendAllText(_logFilePath, entry.Format() + Environment.NewLine);
        }
        catch (IOException)
        {
            // 로그 파일 쓰기 실패는 앱 동작을 막지 않는다
        }
    }
}
