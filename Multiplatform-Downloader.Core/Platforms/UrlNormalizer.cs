using System.Text.RegularExpressions;

namespace Multiplatform_Downloader.Core.Platforms;

/// <summary>
/// 분석 전 URL 정규화(FR-01, FR-N1.3). 순수 함수 — 플랫폼별 추출기가 처리하기 쉬운 형태로 바꾼다.
/// <para>· rednote.com → xiaohongshu.com (전용 extractor 사용)</para>
/// <para>· Facebook <c>/&lt;page&gt;/videos/&lt;id&gt;</c> → <c>facebook.com/watch/?v=&lt;id&gt;</c>
///   (실측: /videos/ 형태는 yt-dlp 'Cannot parse data' 오류 → watch 형태로 우회)</para>
/// <para>· Douyin 피드/모달 <c>douyin.com/jingxuan?modal_id=&lt;id&gt;</c>(및 discover 등) →
///   <c>douyin.com/video/&lt;id&gt;</c> (실측: 모달 URL은 'Unsupported URL', /video/ 형태만 인식)</para>
/// </summary>
public static class UrlNormalizer
{
    private static readonly Regex FacebookVideosPath = new(
        @"^https?://(?:www\.|m\.|web\.)?facebook\.com/[^/?#]+/videos/(?:[^/?#]+/)?(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 도우인 모달 형태: 어떤 douyin.com 경로든 쿼리에 modal_id=<숫자>가 있으면 그 ID가 실제 영상
    private static readonly Regex DouyinModalId = new(
        @"^https?://(?:www\.)?douyin\.com/[^?#]*[?&]modal_id=(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string Normalize(string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return url;

        url = url.Replace("rednote.com", "xiaohongshu.com", StringComparison.OrdinalIgnoreCase);

        var fb = FacebookVideosPath.Match(url);
        if (fb.Success)
            return $"https://www.facebook.com/watch/?v={fb.Groups[1].Value}";

        var dy = DouyinModalId.Match(url);
        if (dy.Success)
            return $"https://www.douyin.com/video/{dy.Groups[1].Value}";

        return url;
    }
}
