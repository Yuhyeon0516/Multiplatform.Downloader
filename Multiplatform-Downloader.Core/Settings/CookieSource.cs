namespace Multiplatform_Downloader.Core.Settings;

/// <summary>
/// 로그인 필요 콘텐츠용 쿠키 소스(FR-06). yt-dlp <c>--cookies-from-browser</c>로 브라우저 쿠키를 읽거나
/// <c>--cookies</c>로 cookies.txt 파일을 사용한다.
/// <para>⚠ 브라우저 쿠키(Chrome/Edge/Firefox)는 <b>해당 브라우저를 닫아야</b> 읽힌다
/// (실행 중이면 쿠키 DB 잠금으로 실패, yt-dlp #7271). Chrome/Edge 127+는 App-Bound Encryption 영향으로
/// 버전에 따라 실패할 수 있으므로, 안 되면 <see cref="CookieFile"/>(cookies.txt) 방식을 사용한다.</para>
/// </summary>
public enum CookieSource
{
    None,
    CookieFile,
    ChromeBrowser,
    EdgeBrowser,
    FirefoxBrowser,
}
