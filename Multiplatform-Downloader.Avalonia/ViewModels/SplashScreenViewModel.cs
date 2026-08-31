using System.Collections.ObjectModel;
using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>기동 스플래시 — WPF 헤드 이식(무수정).</summary>
internal sealed class SplashScreenViewModel : Screen
{
    private const int MaxLogLines = 5;

    private readonly IAppLogger _logger;
    private readonly IClock _clock;

    private string _statusMessage = "시작하는 중...";
    private int _progress;
    private string _version = string.Empty;

    public SplashScreenViewModel(IAppLogger logger, IClock clock)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        DisplayName = "샤샤룽 다운로더";
    }

    public string ProductName { get; } = "샤샤룽 다운로더";
    public string SubTitle { get; } = "YouTube · Instagram · TikTok · 샤오홍슈 영상 다운로더";

    public ObservableCollection<string> LogLines { get; } = [];

    public string Version
    {
        get => _version;
        set { _version = value; NotifyOfPropertyChange(); }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set { _statusMessage = value; NotifyOfPropertyChange(); }
    }

    public int Progress
    {
        get => _progress;
        set { _progress = value; NotifyOfPropertyChange(); }
    }

    public void UpdateStep(string message, int progress)
    {
        StatusMessage = message;
        Progress = Math.Min(progress, 100);

        LogLines.Add($"[{_clock.Now:HH:mm:ss}] {message}");
        while (LogLines.Count > MaxLogLines)
            LogLines.RemoveAt(0);

        _logger.Info("Splash", $"{Progress}% — {message}");
    }
}
