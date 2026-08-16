using System.Globalization;

namespace Multiplatform_Downloader.Core.Platforms;

/// <summary>
/// URL에서 플랫폼을 감지한다(FR-01). http/https만 허용하고 <see cref="Uri.Host"/> 기반으로
/// 정확한 도메인 경계를 검사해 <c>youtube.com.evil</c> 같은 유사 도메인을 차단한다(NFR-10, SSRF 1차 방어).
/// 샤오홍슈는 xiaohongshu.com·rednote.com·xhslink.com을 모두 인식한다.
/// </summary>
public sealed class PlatformDetector
{
    private static readonly (PlatformType Platform, string[] Domains)[] DomainMap =
    {
        (PlatformType.YouTube,      new[] { "youtube.com", "youtu.be", "youtube-nocookie.com" }),
        (PlatformType.Instagram,    new[] { "instagram.com", "instagr.am" }), // instagr.am: 공식 단축 도메인(실측 FAIL 수정, FR-D4.3)
        (PlatformType.TikTok,       new[] { "tiktok.com" }),
        (PlatformType.Xiaohongshu,  new[] { "xiaohongshu.com", "xhslink.com", "rednote.com" }),
        (PlatformType.Threads,      new[] { "threads.net", "threads.com" }), // 자체 폴백 추출(yt-dlp 미지원, FR-N1.8)
        // 신규 플랫폼(FR-N1.1) — 전부 yt-dlp 처리(도우인만 쿠키 필요, FR-N1.4)
        (PlatformType.Facebook,     new[] { "facebook.com", "fb.watch", "fb.com" }),
        (PlatformType.X,            new[] { "x.com", "twitter.com" }),
        (PlatformType.Douyin,       new[] { "douyin.com", "iesdouyin.com" }),
        (PlatformType.Reddit,       new[] { "reddit.com", "redd.it", "redditmedia.com" }),
        (PlatformType.Pinterest,    new[] { "pinterest.com", "pinterest.co.kr", "pinterest.co.uk",
                                            "pinterest.fr", "pinterest.de", "pinterest.jp", "pin.it" }),
    };

    /// <summary>URL의 플랫폼을 반환한다. 미지원·비 http(s)·잘못된 URL은 <see cref="PlatformType.Unknown"/>.</summary>
    public PlatformType Detect(string? url)
    {
        if (!TryGetHttpHost(url, out var host))
            return PlatformType.Unknown;

        foreach (var (platform, domains) in DomainMap)
        {
            foreach (var domain in domains)
            {
                // 정확히 일치하거나 서브도메인(".domain")만 허용 — "youtube.com.evil"은 불일치
                if (host == domain || host.EndsWith("." + domain, StringComparison.Ordinal))
                    return platform;
            }
        }
        return PlatformType.Unknown;
    }

    private static bool TryGetHttpHost(string? url, out string host)
    {
        host = string.Empty;
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return false;

        host = uri.Host.ToLowerInvariant();
        // IDN/punycode 정규화 — 유니코드 유사문자 우회 차단
        try { host = new IdnMapping().GetAscii(host); }
        catch (ArgumentException) { return false; }

        return host.Length > 0;
    }
}
