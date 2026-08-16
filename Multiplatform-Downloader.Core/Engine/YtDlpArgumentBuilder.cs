using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Settings;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>
/// yt-dlp 인자를 조립한다(REFAC-32). 모든 명령에 <c>--ignore-config</c>를 강제하고(NFR-12),
/// 위치 인자(URL) 앞에 <c>--</c> 구분자를 넣어 <c>--exec</c> 등으로의 인자 인젝션을 차단한다.
/// </summary>
public static class YtDlpArgumentBuilder
{
    private static readonly PlatformDetector Detector = new();

    // 틱톡 봇 감지 우회 — 2026-08-10경 배포된 신규 감지가 Windows UA 요청을 전부 차단한다
    // (실측 2026-08-17: Windows UA(140·150)=Unexpected response, Mac UA=정상 · impersonate와 병용 가능.
    //  yt-dlp#17403 미해결이라 자체 우회). 관건은 Chrome 버전이 아니라 Mac 플랫폼 문자열.
    private const string TikTokUserAgent =
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/150.0.0.0 Safari/537.36";

    /// <summary>메타데이터 조회(<c>-J</c>) 인자.</summary>
    public static IReadOnlyList<string> BuildMetadata(string url, string? proxyUrl = null, CookieOptions? cookies = null)
    {
        var args = new List<string> { "-J", "--no-playlist", "--ignore-config" };
        AddCommon(args, url, proxyUrl, cookies);
        AddUrl(args, url);
        return args;
    }

    /// <summary>다운로드 인자(<c>--newline</c> 진행률, 최종 경로 출력, 이어받기).</summary>
    public static IReadOnlyList<string> BuildDownload(DownloadRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var args = new List<string>
        {
            "-f", request.FormatId,
            "-o", request.OutputTemplate,
            "--newline",
            // --print 는 --quiet 를 암시해 진행률 출력을 꺼버린다(실측: [download] 라인 0개).
            // --progress 로 quiet 상태에서도 진행률을 강제 출력한다.
            "--progress",
            "--no-playlist",
            "--ignore-config",
            "--print", "after_move:filepath",
        };
        if (request.Continue)
            args.Add("-c");
        AddCommon(args, request.Url, request.ProxyUrl, new CookieOptions(request.CookieFile, request.CookieFromBrowser));
        AddUrl(args, request.Url);
        return args;
    }

    private static void AddCommon(List<string> args, string url, string? proxyUrl, CookieOptions? cookies)
    {
        // 파이프 출력 UTF-8 강제 — 번들 yt-dlp(PyInstaller)는 PYTHONUTF8 환경변수를 무시(isolated)하고
        // 기본은 로캘 CP949로 인코딩하며 표현 불가 문자(간체자 등)를 '버린다'.
        // Destination/after_move:filepath 경로가 훼손되던 근본 원인(2026-08-03 실증:
        // 기본 = "说它发" 소실 · --encoding utf-8 = 무손실). ProcessRunner는 UTF-8로 읽는다.
        args.Add("--encoding");
        args.Add("utf-8");
        // 브라우저 위장 — TikTok/Instagram/샤오홍슈는 비브라우저 요청을 차단한다(curl_cffi 번들)
        args.Add("--impersonate");
        args.Add("chrome");
        // 틱톡만 Mac UA 명시 — 다른 플랫폼은 검증된 기존 동작을 유지한다
        if (Detector.Detect(url) == PlatformType.TikTok)
        {
            args.Add("--user-agent");
            args.Add(TikTokUserAgent);
        }
        if (!string.IsNullOrWhiteSpace(proxyUrl))
        {
            args.Add("--proxy");
            args.Add(proxyUrl);
        }
        // 브라우저 쿠키 우선(--cookies-from-browser), 없으면 cookies.txt 파일(--cookies)
        if (!string.IsNullOrWhiteSpace(cookies?.FromBrowser))
        {
            args.Add("--cookies-from-browser");
            args.Add(cookies.FromBrowser);
        }
        else if (!string.IsNullOrWhiteSpace(cookies?.CookieFile))
        {
            args.Add("--cookies");
            args.Add(cookies.CookieFile);
        }
    }

    private static void AddUrl(List<string> args, string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        if (url.StartsWith('-'))
            throw new ArgumentException("URL은 '-'로 시작할 수 없습니다 (인자 인젝션 방어).", nameof(url));
        args.Add("--");
        args.Add(url);
    }
}
