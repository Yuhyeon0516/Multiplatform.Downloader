namespace Multiplatform_Downloader.Core.Diagnostics;

public enum AppLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
}

/// <summary>구조화된 로그 항목.</summary>
public sealed record LogEntry(DateTime Timestamp, AppLogLevel Level, string Category, string Message)
{
    public string Format() => $"{Timestamp:HH:mm:ss.fff} [{Level,-7}] {Category}: {Message}";
}

/// <summary>앱 전역 로깅(모든 서비스 동작 추적). UI는 <see cref="Logged"/>를 구독해 실시간 표시한다.</summary>
public interface IAppLogger
{
    void Log(AppLogLevel level, string category, string message);
    IReadOnlyList<LogEntry> Recent { get; }
    event EventHandler<LogEntry>? Logged;
}

/// <summary>레벨별 편의 확장.</summary>
public static class AppLoggerExtensions
{
    public static void Debug(this IAppLogger logger, string category, string message) => logger.Log(AppLogLevel.Debug, category, message);
    public static void Info(this IAppLogger logger, string category, string message) => logger.Log(AppLogLevel.Info, category, message);
    public static void Warning(this IAppLogger logger, string category, string message) => logger.Log(AppLogLevel.Warning, category, message);
    public static void Error(this IAppLogger logger, string category, string message) => logger.Log(AppLogLevel.Error, category, message);
}

/// <summary>로깅이 필요 없는 곳의 무동작 구현.</summary>
public sealed class NullAppLogger : IAppLogger
{
    public static readonly NullAppLogger Instance = new();
    public IReadOnlyList<LogEntry> Recent => [];
    public event EventHandler<LogEntry>? Logged { add { } remove { } }
    public void Log(AppLogLevel level, string category, string message) { }
}
