using Caliburn.Micro;
using Multiplatform_Downloader.Core.Settings;
using Multiplatform_Downloader.Services;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>설정 화면(WF-03, FR-06). 저장 시 ISettingsService로 영속화.</summary>
internal sealed class SettingsViewModel : Screen
{
    private readonly ISettingsService _settings;

    private string _downloadFolder;
    private int _maxConcurrent;
    private int _maxQueueItems;
    private QualityPreference _defaultQuality;
    private bool _launchOnStartup;
    private bool _startMinimized;
    private bool _closeToTray;
    private bool _notifyOnComplete;
    private bool _autoStartDownload;
    private AppTheme _theme;
    private CookieSource _cookieSource;
    private string? _cookieFilePath;
    private int _analysisRetryCount;

    public SettingsViewModel(ISettingsService settings)
    {
        _settings = settings;
        DisplayName = "설정";

        var c = settings.Current;
        _downloadFolder = c.DownloadFolder;
        _maxConcurrent = c.MaxConcurrent;
        _maxQueueItems = c.MaxQueueItems;
        _defaultQuality = c.DefaultQuality;
        _launchOnStartup = c.LaunchOnStartup;
        _startMinimized = c.StartMinimized;
        _closeToTray = c.CloseToTray;
        _notifyOnComplete = c.NotifyOnComplete;
        _autoStartDownload = c.AutoStartDownload;
        _theme = c.Theme;
        _cookieSource = c.CookieSource;
        _cookieFilePath = c.CookieFilePath;
        _analysisRetryCount = c.AnalysisRetryCount;
    }

    public IReadOnlyList<int> ConcurrentOptions { get; } = [1, 2, 3, 4, 5, 6, 7, 8];
    public IReadOnlyList<int> RetryOptions { get; } = [0, 1, 2, 3, 4, 5];
    public IReadOnlyList<QualityPreference> QualityOptions { get; } = [QualityPreference.Best, QualityPreference.Worst, QualityPreference.AudioOnly];
    public IReadOnlyList<AppTheme> ThemeOptions { get; } = [AppTheme.System, AppTheme.Light, AppTheme.Dark];
    public IReadOnlyList<CookieSource> CookieOptions { get; } =
        [CookieSource.None, CookieSource.CookieFile, CookieSource.ChromeBrowser, CookieSource.EdgeBrowser, CookieSource.FirefoxBrowser];

    public string DownloadFolder { get => _downloadFolder; set { _downloadFolder = value; NotifyOfPropertyChange(); } }
    public int MaxConcurrent { get => _maxConcurrent; set { _maxConcurrent = value; NotifyOfPropertyChange(); } }
    public int MaxQueueItems { get => _maxQueueItems; set { _maxQueueItems = value; NotifyOfPropertyChange(); } }
    public QualityPreference DefaultQuality { get => _defaultQuality; set { _defaultQuality = value; NotifyOfPropertyChange(); } }
    public bool LaunchOnStartup { get => _launchOnStartup; set { _launchOnStartup = value; NotifyOfPropertyChange(); } }
    public bool StartMinimized { get => _startMinimized; set { _startMinimized = value; NotifyOfPropertyChange(); } }
    public bool CloseToTray { get => _closeToTray; set { _closeToTray = value; NotifyOfPropertyChange(); } }
    public bool NotifyOnComplete { get => _notifyOnComplete; set { _notifyOnComplete = value; NotifyOfPropertyChange(); } }
    public bool AutoStartDownload { get => _autoStartDownload; set { _autoStartDownload = value; NotifyOfPropertyChange(); } }
    public AppTheme Theme { get => _theme; set { _theme = value; NotifyOfPropertyChange(); } }
    public CookieSource CookieSource
    {
        get => _cookieSource;
        set
        {
            _cookieSource = value;
            NotifyOfPropertyChange();
            NotifyOfPropertyChange(nameof(IsCookieFile));
            NotifyOfPropertyChange(nameof(IsBrowserCookie));
        }
    }
    public string? CookieFilePath { get => _cookieFilePath; set { _cookieFilePath = value; NotifyOfPropertyChange(); } }

    /// <summary>등록(분석)·썸네일 실패 자동 재시도 횟수(FR-D2.5) — 네트워크 계열 실패에만 적용.</summary>
    public int AnalysisRetryCount { get => _analysisRetryCount; set { _analysisRetryCount = value; NotifyOfPropertyChange(); } }

    public bool IsCookieFile => CookieSource == CookieSource.CookieFile;

    /// <summary>브라우저 쿠키(Chrome/Edge/Firefox) 선택 시 "브라우저를 닫아야 함" 안내를 노출한다.</summary>
    public bool IsBrowserCookie =>
        CookieSource is CookieSource.ChromeBrowser or CookieSource.EdgeBrowser or CookieSource.FirefoxBrowser;

    public string CookieHint =>
        "주의: 브라우저 쿠키는 해당 브라우저를 완전히 종료한 뒤 다운로드해야 읽힙니다 (실행 중이면 쿠키 잠금으로 실패). "
        + "Chrome/Edge 최신 버전에서 실패하면 cookies.txt 파일 방식을 사용하세요.";

    public void BrowseFolder()
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "다운로드 폴더 선택" };
        if (dialog.ShowDialog() == true)
            DownloadFolder = dialog.FolderName;
    }

    public void BrowseCookieFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Title = "cookies.txt 선택", Filter = "쿠키 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*" };
        if (dialog.ShowDialog() == true)
            CookieFilePath = dialog.FileName;
    }

    public async Task Save()
    {
        var c = _settings.Current;
        c.DownloadFolder = DownloadFolder;
        c.MaxConcurrent = MaxConcurrent;
        c.MaxQueueItems = MaxQueueItems;
        c.DefaultQuality = DefaultQuality;
        c.LaunchOnStartup = LaunchOnStartup;
        c.StartMinimized = StartMinimized;
        c.CloseToTray = CloseToTray;
        c.NotifyOnComplete = NotifyOnComplete;
        c.AutoStartDownload = AutoStartDownload;
        c.Theme = Theme;
        c.CookieSource = CookieSource;
        c.CookieFilePath = CookieFilePath;
        c.AnalysisRetryCount = AnalysisRetryCount;

        await _settings.SaveAsync();
        ThemeService.Apply(Theme); // 라이트/다크/시스템 즉시 반영
        await TryCloseAsync(true);
    }

    public async Task Cancel() => await TryCloseAsync(false);
}
