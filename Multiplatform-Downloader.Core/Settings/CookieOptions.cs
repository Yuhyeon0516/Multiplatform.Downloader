namespace Multiplatform_Downloader.Core.Settings;

/// <summary>
/// yt-dlp에 전달할 쿠키 지정(FR-06). <see cref="FromBrowser"/>가 있으면 브라우저 쿠키
/// (<c>--cookies-from-browser</c>), 없고 <see cref="CookieFile"/>이 있으면 파일(<c>--cookies</c>)을 사용한다.
/// 둘 다 없으면 익명 접근.
/// </summary>
public sealed record CookieOptions(string? CookieFile = null, string? FromBrowser = null)
{
    /// <summary>쿠키 미사용(익명).</summary>
    public static readonly CookieOptions None = new();

    /// <summary>브라우저 또는 파일 쿠키가 지정되었는지.</summary>
    public bool HasCookies =>
        !string.IsNullOrWhiteSpace(FromBrowser) || !string.IsNullOrWhiteSpace(CookieFile);
}
