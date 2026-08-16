using Caliburn.Micro;
using Multiplatform_Downloader.Core.Abstractions;
using Multiplatform_Downloader.Core.Diagnostics;
using System;
using System.Collections.ObjectModel;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>
/// 기동 스플래시(NFR). 단계별 프로그레스바 + 상태 메시지 + 미니 로딩 로그를 보여준다.
/// Bootstrapper가 초기화 단계마다 <see cref="UpdateStep"/>을 호출하고, 완료 후 닫는다.
/// </summary>
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

    /// <summary>스플래시 하단 미니 콘솔에 보여줄 최근 로딩 로그.</summary>
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

    /// <summary>초기화 단계를 갱신한다. UI 스레드에서 호출한다.</summary>
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
