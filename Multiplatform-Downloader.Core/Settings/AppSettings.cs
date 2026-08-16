namespace Multiplatform_Downloader.Core.Settings;

/// <summary>영속 설정(FR-06). 값 범위는 <c>ISettingsService</c>가 저장/로드 시 정규화한다.</summary>
public sealed class AppSettings
{
    /// <summary>동시 다운로드 허용 범위(FR-05).</summary>
    public const int MinConcurrent = 1;
    public const int MaxConcurrentLimit = 8;
    public const int MinQueueItems = 1;
    public const int MaxQueueLimit = 50;
    /// <summary>등록(분석)·썸네일 재시도 횟수 범위(FR-D1.4/D2.1/D2.5).</summary>
    public const int MinRetry = 0;
    public const int MaxRetry = 5;

    public string DownloadFolder { get; set; } = DefaultDownloadFolder();
    public int MaxConcurrent { get; set; } = 3;
    public int MaxQueueItems { get; set; } = 15;
    public QualityPreference DefaultQuality { get; set; } = QualityPreference.Best;
    public bool LaunchOnStartup { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool NotifyOnComplete { get; set; } = true;

    /// <summary>등록하면 분석 후 자동으로 다운로드를 시작한다(FR-N3.1). 기본 false — 기존 '분석 후 대기' 동작 유지.</summary>
    public bool AutoStartDownload { get; set; }

    public AppTheme Theme { get; set; } = AppTheme.System;
    public CookieSource CookieSource { get; set; } = CookieSource.None;
    public string? CookieFilePath { get; set; }

    /// <summary>등록(분석)·썸네일 실패 시 자동 재시도 횟수 — 네트워크 계열만(FR-D2.1, NFR-D2).</summary>
    public int AnalysisRetryCount { get; set; } = 2;

    public static string DefaultDownloadFolder() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    /// <summary>현재 쿠키 설정을 yt-dlp 인자용 <see cref="CookieOptions"/>로 변환한다(FR-06).</summary>
    public CookieOptions ResolveCookies() => CookieSource switch
    {
        CookieSource.CookieFile => string.IsNullOrWhiteSpace(CookieFilePath)
            ? CookieOptions.None
            : new CookieOptions(CookieFile: CookieFilePath),
        CookieSource.ChromeBrowser => new CookieOptions(FromBrowser: "chrome"),
        CookieSource.EdgeBrowser => new CookieOptions(FromBrowser: "edge"),
        CookieSource.FirefoxBrowser => new CookieOptions(FromBrowser: "firefox"),
        _ => CookieOptions.None,
    };

    /// <summary>값 범위를 클램프한 새 인스턴스를 만든다(파괴적 변경 없이).</summary>
    public AppSettings Normalized()
    {
        return new AppSettings
        {
            DownloadFolder = string.IsNullOrWhiteSpace(DownloadFolder) ? DefaultDownloadFolder() : DownloadFolder,
            MaxConcurrent = Math.Clamp(MaxConcurrent, MinConcurrent, MaxConcurrentLimit),
            MaxQueueItems = Math.Clamp(MaxQueueItems, MinQueueItems, MaxQueueLimit),
            DefaultQuality = DefaultQuality,
            LaunchOnStartup = LaunchOnStartup,
            StartMinimized = StartMinimized,
            CloseToTray = CloseToTray,
            NotifyOnComplete = NotifyOnComplete,
            AutoStartDownload = AutoStartDownload,
            Theme = Theme,
            CookieSource = CookieSource,
            CookieFilePath = CookieFilePath,
            AnalysisRetryCount = Math.Clamp(AnalysisRetryCount, MinRetry, MaxRetry),
        };
    }
}
