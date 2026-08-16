using Multiplatform_Downloader.Core.Engine;

namespace Multiplatform_Downloader.Tests.Engine;

/// <summary>NFR-D4: 실측 stderr 원문 7종(2026-08-02 채집)을 픽스처로 분류기를 검증한다.</summary>
public class ExtractionErrorClassifierTests
{
    // ── 실측 원문 픽스처 ──

    private const string XhsError =
        "ERROR: [XiaoHongShu] 6a609b5f000000000100271d: No video formats found!; please report this issue on  https://github.com/yt-dlp/yt-dlp/issues";

    private const string InstagramError =
        "ERROR: [Instagram] ZZZZZZZZZZZ: Instagram sent an empty media response. Check if this post is accessible in your browser without being logged-in. If it is not, then use --cookies-from-browser or --cookies for the authentication.";

    private const string YoutubeUnavailable =
        "ERROR: [youtube] 00000000000: Video unavailable";

    private const string NetworkError =
        "ERROR: [youtube] jNQXAC9IVRw: Unable to download API page: Failed to perform, curl: (7) Failed to connect to 127.0.0.1 port 1 after 2016 ms: Could not connect to server. (caused by TransportError('...'))";

    private const string UnsupportedUrl =
        "ERROR: Unsupported URL: https://www.tiktok.com/foryou";

    private const string TikTokBlocked =
        "ERROR: [TikTok] 1: Your IP address is blocked from accessing this post";

    [Fact]
    public void should_classify_xhs_no_formats_as_link_expired()
    {
        var f = ExtractionErrorClassifier.Classify(XhsError);
        Assert.Equal(ExtractionFailureKind.XhsLinkExpired, f.Kind);
        Assert.False(f.IsRetryable);
        Assert.Contains("링크 만료", f.UserMessage);
    }

    [Fact]
    public void should_classify_instagram_empty_response_as_login_or_gone()
    {
        var f = ExtractionErrorClassifier.Classify(InstagramError);
        Assert.Equal(ExtractionFailureKind.InstagramLoginOrGone, f.Kind);
        Assert.False(f.IsRetryable);
    }

    [Fact]
    public void should_classify_youtube_video_unavailable()
    {
        var f = ExtractionErrorClassifier.Classify(YoutubeUnavailable);
        Assert.Equal(ExtractionFailureKind.VideoUnavailable, f.Kind);
    }

    [Fact]
    public void should_classify_dead_proxy_as_network_and_retryable()
    {
        var f = ExtractionErrorClassifier.Classify(NetworkError);
        Assert.Equal(ExtractionFailureKind.Network, f.Kind);
        Assert.True(f.IsRetryable); // NFR-D2: 네트워크만 재시도
    }

    [Fact]
    public void should_classify_feed_page_as_unsupported_url()
    {
        var f = ExtractionErrorClassifier.Classify(UnsupportedUrl);
        Assert.Equal(ExtractionFailureKind.UnsupportedUrl, f.Kind);
        Assert.False(f.IsRetryable);
        Assert.Contains("틱톡 피드", f.UserMessage); // 틱톡 URL은 행동 유도 특화 안내(실사용 보고 반영)
    }

    [Fact]
    public void should_guide_facebook_feed_on_unsupported_url()
    {
        // 실측: 피드에서 우클릭 시 facebook.com/ 홈이 전송 → 개별 영상 안내
        var f = ExtractionErrorClassifier.Classify("ERROR: Unsupported URL: https://www.facebook.com/?_fb_noscript=1");
        Assert.Equal(ExtractionFailureKind.UnsupportedUrl, f.Kind);
        Assert.Contains("페이스북 피드", f.UserMessage);
        Assert.Contains("/reel/", f.UserMessage);
    }

    [Fact]
    public void should_guide_reddit_listing_on_unsupported_url()
    {
        // 실측: 서브레딧/목록 페이지(r/hanguk/) → 개별 영상 게시물 안내
        var f = ExtractionErrorClassifier.Classify("ERROR: Unsupported URL: https://www.reddit.com/r/hanguk/");
        Assert.Equal(ExtractionFailureKind.UnsupportedUrl, f.Kind);
        Assert.Contains("개별 영상 게시물", f.UserMessage);
    }

    [Fact]
    public void should_use_generic_message_for_non_tiktok_unsupported_url()
    {
        var f = ExtractionErrorClassifier.Classify("ERROR: Unsupported URL: https://www.instagram.com/explore/");
        Assert.Equal(ExtractionFailureKind.UnsupportedUrl, f.Kind);
        Assert.Contains("개별 영상 링크", f.UserMessage);
        Assert.DoesNotContain("틱톡", f.UserMessage);
    }

    [Fact]
    public void should_classify_tiktok_ip_blocked_as_blocked_or_gone_not_network()
    {
        // 실측: 존재하지 않는 게시물도 이 메시지를 낸다 — 'IP 차단'만으로 매핑하면 오분류
        var f = ExtractionErrorClassifier.Classify(TikTokBlocked);
        Assert.Equal(ExtractionFailureKind.TikTokBlockedOrGone, f.Kind);
        Assert.False(f.IsRetryable);
        Assert.Contains("게시물", f.UserMessage);
    }

    // ── 2026-08-02 실사이트 검증에서 채집한 추가 시그니처 ──

    private const string InstagramPhotoPost =
        "ERROR: [Instagram] DZJDbdKggtc: No video formats found!; please report this issue on  https://github.com/yt-dlp/yt-dlp/issues?q= , filling out the appropriate issue template. Confirm you are on the latest version using  yt-dlp -U";

    private const string YoutubeBotCheck =
        "ERROR: [youtube] QCOcR8H4jfo: Sign in to confirm you’re not a bot. Use --cookies-from-browser or --cookies for the authentication. See  https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-pass-cookies-to-yt-dlp  for how to manually pass cookies.";

    [Fact]
    public void should_classify_instagram_photo_post_as_no_video_content()
    {
        // 실측: 사진(캐러셀) 게시물은 하위 미디어마다 No video formats found — XHS 규칙과 태그로 구분
        var f = ExtractionErrorClassifier.Classify(InstagramPhotoPost);
        Assert.Equal(ExtractionFailureKind.NoVideoContent, f.Kind);
        Assert.False(f.IsRetryable);
        Assert.Contains("사진 게시물", f.UserMessage);
    }

    [Fact]
    public void should_classify_youtube_bot_check_as_login_or_bot_check()
    {
        // 실측: 반복 요청 후 IP 단위 봇 확인 — 자동 재시도하면 밴 위험이라 재시도 비대상
        var f = ExtractionErrorClassifier.Classify(YoutubeBotCheck);
        Assert.Equal(ExtractionFailureKind.LoginOrBotCheck, f.Kind);
        Assert.False(f.IsRetryable);
        Assert.Contains("봇 확인", f.UserMessage);
    }

    [Fact]
    public void should_classify_instagram_http_400_as_video_unavailable()
    {
        // 실측: 로그인 세션으로 없는 게시물 조회 → 400 Bad Request (비로그인은 empty media response)
        var f = ExtractionErrorClassifier.Classify(
            "ERROR: [Instagram] ZZZZZZZZZZZ: Video info extraction failed: HTTP Error 400: Bad Request (caused by <HTTPError 400: Bad Request>)");
        Assert.Equal(ExtractionFailureKind.VideoUnavailable, f.Kind);
        Assert.Contains("찾을 수 없습니다", f.UserMessage);
    }

    [Fact]
    public void should_classify_douyin_fresh_cookies_as_login_required()
    {
        // 실측(2026-08-02): 도우인은 방문 쿠키 없으면 100% 이 메시지 → [로그인] 창으로 해결
        var f = ExtractionErrorClassifier.Classify(
            "ERROR: [Douyin] 7604129988555574538: Fresh cookies (not necessarily logged in) are needed");
        Assert.Equal(ExtractionFailureKind.LoginOrBotCheck, f.Kind);
        Assert.Contains("도우인", f.UserMessage);
    }

    [Fact]
    public void should_classify_pinterest_404_as_video_unavailable()
    {
        var f = ExtractionErrorClassifier.Classify(
            "ERROR: [Pinterest] 824721750502199491: Unable to download JSON metadata: HTTP Error 404: Not Found");
        Assert.Equal(ExtractionFailureKind.VideoUnavailable, f.Kind);
        Assert.Contains("핀을 찾을 수 없습니다", f.UserMessage);
    }

    [Fact]
    public void should_classify_age_restricted_as_login_with_age_message()
    {
        // 실측(Tq92D6wQ1mg): 연령 제한도 'Sign in to confirm' — 같은 해결 경로, 문구만 구분
        var f = ExtractionErrorClassifier.Classify(
            "ERROR: [youtube] Tq92D6wQ1mg: Sign in to confirm your age. This video may be inappropriate for some users.");
        Assert.Equal(ExtractionFailureKind.LoginOrBotCheck, f.Kind);
        Assert.Contains("연령 확인", f.UserMessage);
    }

    [Fact]
    public void should_preserve_raw_text_for_unknown_errors()
    {
        var f = ExtractionErrorClassifier.Classify("ERROR: something brand new happened");
        Assert.Equal(ExtractionFailureKind.Unknown, f.Kind);
        Assert.Contains("something brand new", f.UserMessage); // 원문 보존
    }

    [Fact]
    public void should_extract_last_error_line_skipping_warnings()
    {
        // 실측: 네트워크 케이스는 WARNING 재시도 6줄+가 선행 — 마지막 ERROR만 취해야 함
        string[] stderr =
        [
            "WARNING: [youtube] Failed to perform, curl: (7)... Retrying (1/3)...",
            "WARNING: [youtube] Failed to perform, curl: (7)... Retrying (2/3)...",
            "WARNING: [youtube] Unable to download webpage: ... Giving up after 3 retries",
            NetworkError,
        ];

        var f = ExtractionErrorClassifier.ClassifyStderr(stderr);

        Assert.Equal(ExtractionFailureKind.Network, f.Kind);
        Assert.Equal(NetworkError, f.RawErrorLine);
    }

    [Fact]
    public void should_prioritize_network_over_extractor_tags()
    {
        // 네트워크 오류 라인에 추출기 태그가 섞여도 네트워크가 우선 (실측 순서)
        var f = ExtractionErrorClassifier.Classify(
            "ERROR: [Instagram] abc: Unable to download API page: Failed to connect");
        Assert.Equal(ExtractionFailureKind.Network, f.Kind);
    }

    [Fact]
    public void should_handle_empty_stderr()
    {
        var f = ExtractionErrorClassifier.ClassifyStderr([]);
        Assert.Equal(ExtractionFailureKind.Unknown, f.Kind);
    }
}
