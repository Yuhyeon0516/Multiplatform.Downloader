using Multiplatform_Downloader.Avalonia.Mvvm;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.Avalonia.ViewModels;

/// <summary>
/// 앱 내 로그인/봇 확인 창(FR-L3~L4) — WPF 헤드 이식(무수정에 가까움).
/// 뷰(WKWebView 호스트)가 AttachCookieSource로 쿠키 열람 함수를 연결한다 — VM은 웹뷰에 직접 의존하지 않는다.
/// </summary>
public sealed class LoginBrowserViewModel : Screen
{
    private readonly IAppLogger _logger;
    private readonly string _cookieFilePath;
    private Func<Task<IReadOnlyList<CookieRecord>>>? _cookieSource;

    public LoginBrowserViewModel(string startUrl, IAppLogger logger, string? cookieFilePath = null)
    {
        StartUrl = startUrl ?? throw new ArgumentNullException(nameof(startUrl));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cookieFilePath = cookieFilePath ?? DefaultCookieFilePath;
        DisplayName = "로그인 / 확인 — 샤샤룽 다운로더";
    }

    /// <summary>로그인 쿠키 저장 경로 — 사용자 데이터 폴더 전용(NFR-L2).</summary>
    public static string DefaultCookieFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Multiplatform-Downloader", "login-cookies.txt");

    public string StartUrl { get; }

    public bool CookiesSaved { get; private set; }

    public string StatusText { get; private set; } =
        "아래 화면에서 로그인 또는 확인을 마친 뒤 [완료]를 누르세요.";

    public void AttachCookieSource(Func<Task<IReadOnlyList<CookieRecord>>> source)
        => _cookieSource = source ?? throw new ArgumentNullException(nameof(source));

    public void ReportBrowserUnavailable(string message)
    {
        StatusText = message;
        NotifyOfPropertyChange(nameof(StatusText));
    }

    public async Task Complete()
    {
        if (_cookieSource is null)
        {
            StatusText = "브라우저가 준비되지 않았습니다 — 잠시 후 다시 시도하세요.";
            NotifyOfPropertyChange(nameof(StatusText));
            return;
        }
        try
        {
            var cookies = await _cookieSource();
            var dir = Path.GetDirectoryName(_cookieFilePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);
            await File.WriteAllTextAsync(_cookieFilePath, CookieFileWriter.Serialize(cookies));
            CookiesSaved = true;
            _logger.Info("Login", $"로그인 쿠키 {cookies.Count}개 저장: {_cookieFilePath}"); // 값 미기록(NFR-L1)
            await TryCloseAsync(true);
        }
        catch (Exception ex)
        {
            _logger.Error("Login", $"쿠키 저장 실패: {ex.GetType().Name} {ex.Message}");
            StatusText = "쿠키 저장에 실패했습니다 — 로그를 확인하세요.";
            NotifyOfPropertyChange(nameof(StatusText));
        }
    }

    public Task Cancel() => TryCloseAsync(false);
}
