using System.IO;
using Caliburn.Micro;
using Multiplatform_Downloader.Core.Diagnostics;
using Multiplatform_Downloader.Core.Net;

namespace Multiplatform_Downloader.ViewModels;

/// <summary>
/// 앱 내 로그인/봇 확인 창(FR-L3~L4). 실제 사람이 WebView2에서 로그인·확인을 마치고 [완료]를
/// 누르면 세션 쿠키를 Netscape cookies.txt로 저장한다. 쿠키 값은 로그에 남기지 않는다(NFR-L1).
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

    /// <summary>로그인 쿠키 저장 경로 — 사용자 AppData 전용(NFR-L2).</summary>
    public static string DefaultCookieFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Multiplatform-Downloader", "login-cookies.txt");

    public string StartUrl { get; }

    /// <summary>[완료]로 쿠키가 저장되었는지 — 호출측이 설정 갱신·재시도 여부를 판단.</summary>
    public bool CookiesSaved { get; private set; }

    /// <summary>WebView2 초기화 실패 등으로 브라우저를 쓸 수 없는 상태의 사용자 안내.</summary>
    public string StatusText { get; private set; } =
        "아래 화면에서 로그인 또는 확인을 마친 뒤 [완료]를 누르세요.";

    /// <summary>View(WebView2 호스트)가 쿠키 열람 함수를 연결한다 — VM은 WebView2에 직접 의존하지 않는다.</summary>
    public void AttachCookieSource(Func<Task<IReadOnlyList<CookieRecord>>> source)
        => _cookieSource = source ?? throw new ArgumentNullException(nameof(source));

    /// <summary>브라우저 초기화 실패 보고(런타임 부재 등) — 안내만 바꾸고 창은 유지한다.</summary>
    public void ReportBrowserUnavailable(string message)
    {
        StatusText = message;
        NotifyOfPropertyChange(nameof(StatusText));
    }

    public async Task Complete()
    {
        if (_cookieSource is null)
        {
            StatusText = "브라우저가 준비되지 않았습니다 — WebView2 런타임 설치 후 다시 시도하세요.";
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
