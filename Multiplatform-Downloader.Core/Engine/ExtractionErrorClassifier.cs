namespace Multiplatform_Downloader.Core.Engine;

/// <summary>추출 실패 분류(FR-D2.2). 값은 실측된 yt-dlp stderr 시그니처에서 도출.</summary>
public enum ExtractionFailureKind
{
    /// <summary>네트워크 오류/차단 — 자동 재시도 대상(NFR-D2).</summary>
    Network,
    /// <summary>영상이 아닌 페이지 등 지원하지 않는 링크.</summary>
    UnsupportedUrl,
    /// <summary>샤오홍슈 링크 만료/열람 불가(토큰 문제와 게시물 문제는 stderr로 구분 불가 — 실측).</summary>
    XhsLinkExpired,
    /// <summary>Instagram 로그인 필요 또는 삭제·비공개(두 원인의 시그니처 동일 — 실측).</summary>
    InstagramLoginOrGone,
    /// <summary>삭제되었거나 비공개인 영상.</summary>
    VideoUnavailable,
    /// <summary>TikTok 게시물 없음/접근 불가 — '없는 게시물'도 IP blocked 메시지를 냄(실측).</summary>
    TikTokBlockedOrGone,
    /// <summary>봇 확인/로그인 요구(실측: YouTube 'Sign in to confirm you're not a bot' — IP 단위 일시 차단).</summary>
    LoginOrBotCheck,
    /// <summary>영상이 없는 게시물(실측: Instagram 사진 게시물 → 'No video formats found').</summary>
    NoVideoContent,
    /// <summary>일시적 추출 실패 — 재시도로 회복 가능(실측 2026-08-30: TikTok JS 챌린지가 간헐 실패,
    /// 'Unable to extract universal data for rehydration'. 같은 URL이 프로세스 재실행마다 ~35% 성공).</summary>
    TransientExtraction,
    /// <summary>분류 불가 — 원문 보존.</summary>
    Unknown,
}

/// <summary>분류 결과. <see cref="UserMessage"/>는 카드에 표시할 한국어 문구.</summary>
public sealed record ExtractionFailure(ExtractionFailureKind Kind, string UserMessage, string RawErrorLine)
{
    /// <summary>자동 재시도 대상인지(네트워크 계열 + 일시적 추출 실패 — NFR-D2).</summary>
    public bool IsRetryable => Kind is ExtractionFailureKind.Network or ExtractionFailureKind.TransientExtraction;
}

/// <summary>
/// yt-dlp stderr를 사용자 표시용으로 분류한다(FR-D2.2). 순수 함수 — 실측 stderr 7종을 픽스처로 검증(NFR-D4).
/// <para>실측 사실: 실패 시 exit=1 동일·stdout "null" — 분류 정보는 stderr의 <b>마지막 ERROR 라인</b>에만 존재.
/// 네트워크 실패는 WARNING 재시도 로그 6줄+가 선행되므로 앞줄 파싱은 오분류를 낳는다.</para>
/// </summary>
public static class ExtractionErrorClassifier
{
    /// <summary>stderr 라인들에서 마지막 "ERROR:" 라인을 찾는다. 없으면 마지막 비어있지 않은 라인.</summary>
    public static string ExtractLastErrorLine(IEnumerable<string> stderrLines)
    {
        string? lastError = null;
        string? lastNonEmpty = null;
        foreach (var line in stderrLines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;
            lastNonEmpty = line;
            if (line.TrimStart().StartsWith("ERROR:", StringComparison.OrdinalIgnoreCase))
                lastError = line;
        }
        return lastError ?? lastNonEmpty ?? string.Empty;
    }

    /// <summary>마지막 ERROR 라인을 분류한다. 우선순위 고정: 네트워크 → Unsupported → 추출기별 → 기타(실측 기반 오분류 최소화 순서).</summary>
    public static ExtractionFailure Classify(string? lastErrorLine)
    {
        var line = lastErrorLine ?? string.Empty;

        // ① 네트워크 — 'Unable to download …' + curl/TransportError 계열 (실측: 죽은 프록시).
        //    단, HTTP 4xx(404/400 등)는 '삭제·잘못된 링크'라는 확정 실패이므로 네트워크에서 제외한다
        //    (실측: Pinterest 'Unable to download JSON metadata: HTTP Error 404' 오분류 방지).
        var isHttp4xx = Contains(line, "HTTP Error 4");
        if (!isHttp4xx &&
            (Contains(line, "Unable to download") || Contains(line, "TransportError")
             || Contains(line, "Failed to connect") || Contains(line, "Connection refused")
             || Contains(line, "timed out") || Contains(line, "getaddrinfo")))
            return new(ExtractionFailureKind.Network, "네트워크 오류 — 연결·프록시를 확인하세요", line);

        // ② 지원하지 않는 링크 (실측: tiktok.com/foryou → 'ERROR: Unsupported URL:')
        if (Contains(line, "Unsupported URL"))
        {
            // 피드/홈 URL 특화 안내 — 실사용 보고: 피드에서 개별 영상이 아닌 홈 URL이 전송됨
            string unsupportedMessage;
            if (Contains(line, "tiktok.com"))
                unsupportedMessage = "틱톡 피드 화면입니다 — 영상을 클릭해 연 뒤(주소에 /video/ 포함) 다시 시도하세요";
            else if (Contains(line, "facebook.com") || Contains(line, "fb.watch"))
                unsupportedMessage = "페이스북 피드 화면입니다 — 영상을 클릭해 연 뒤(주소에 /reel/ 또는 /watch/ 포함) 다시 시도하세요";
            else if (Contains(line, "reddit.com") && !Contains(line, "/comments/"))
                unsupportedMessage = "서브레딧/목록 페이지입니다 — 개별 영상 게시물(/comments/...)을 여세요";
            else
                unsupportedMessage = "지원하지 않는 링크 — 개별 영상 링크를 입력하세요";
            return new(ExtractionFailureKind.UnsupportedUrl, unsupportedMessage, line);
        }

        // ③ 추출기 태그별 규칙 (실측 시그니처)
        if (Contains(line, "[XiaoHongShu]") && Contains(line, "No video formats found"))
            return new(ExtractionFailureKind.XhsLinkExpired, "링크 만료 또는 열람 불가 — 새 공유 링크로 다시 시도하세요", line);

        // 실측(2026-08-02): 인스타 사진(캐러셀) 게시물은 하위 미디어마다 'No video formats found'를 낸다
        if (Contains(line, "[Instagram]") && Contains(line, "No video formats found"))
            return new(ExtractionFailureKind.NoVideoContent, "영상이 없는 게시물입니다 (사진 게시물) — 릴스·영상 링크를 입력하세요", line);

        // 실측(2026-08-02): 로그인 세션으로 존재하지 않는 게시물 조회 시 HTTP 400
        if (Contains(line, "[Instagram]") && Contains(line, "HTTP Error 400"))
            return new(ExtractionFailureKind.VideoUnavailable, "게시물을 찾을 수 없습니다 — 삭제되었거나 잘못된 링크입니다", line);

        // 실측(2026-08-02): 도우인은 방문 쿠키가 없으면 100% 이 메시지 → [로그인] 창으로 douyin.com 방문 쿠키 확보(FR-N1.4)
        if (Contains(line, "[Douyin]") && Contains(line, "Fresh cookies"))
            return new(ExtractionFailureKind.LoginOrBotCheck,
                "도우인은 브라우저 쿠키가 필요합니다 — [로그인]으로 도우인을 한 번 연 뒤 다시 시도하세요", line);

        // 실측(2026-08-02): 삭제된 핀은 JSON metadata 404
        if (Contains(line, "[Pinterest]") && Contains(line, "HTTP Error 404"))
            return new(ExtractionFailureKind.VideoUnavailable, "핀을 찾을 수 없습니다 — 삭제되었거나 잘못된 링크입니다", line);

        // 실측(2026-08-02): 연령 제한('your age')과 봇 확인('not a bot') 모두 'Sign in to confirm' —
        // 둘 다 [로그인] 버튼으로 해결 가능(LoginOrBotCheck), 안내 문구만 구분
        if (Contains(line, "Sign in to confirm"))
        {
            var signInMessage = Contains(line, "your age")
                ? "연령 확인이 필요한 영상입니다 — [로그인]으로 로그인하면 받을 수 있습니다"
                : "봇 확인 차단 — [로그인]으로 해결하거나 잠시 후 재시도하세요";
            return new(ExtractionFailureKind.LoginOrBotCheck, signInMessage, line);
        }

        if (Contains(line, "[Instagram]") &&
            (Contains(line, "empty media response") || Contains(line, "not granting access") || Contains(line, "--cookies")))
            return new(ExtractionFailureKind.InstagramLoginOrGone, "로그인이 필요하거나 삭제·비공개된 게시물입니다 (설정에서 쿠키 지정 가능)", line);

        if (Contains(line, "[TikTok]") && Contains(line, "IP address is blocked"))
            return new(ExtractionFailureKind.TikTokBlockedOrGone, "게시물이 없거나 접근할 수 없습니다 (지역·IP 제한 가능)", line);

        // 실측(2026-08-30): TikTok의 새 JS 챌린지가 간헐 실패 → 세 가지 시그니처로 나타남
        // ('Unable to extract universal data for rehydration' / 'Unexpected response from webpage request' /
        // challenge). 같은 URL이 프로세스 재실행마다 ~35% 성공하는 순수 일시 오류이므로 재시도로 회복(FR-D2).
        if (Contains(line, "[TikTok]") &&
            (Contains(line, "Unable to extract universal data for rehydration")
             || Contains(line, "Unexpected response from webpage request")
             || Contains(line, "challenge")))
            return new(ExtractionFailureKind.TransientExtraction, "틱톡 확인 절차 중 — 자동으로 다시 시도합니다", line);

        if (Contains(line, "Video unavailable") || Contains(line, "not available"))
            return new(ExtractionFailureKind.VideoUnavailable, "삭제되었거나 비공개인 영상입니다", line);

        // ④ 기타 — 원문 보존(진단 가능성 유지)
        var trimmed = line.Trim();
        var message = trimmed.Length > 0 ? trimmed : "알 수 없는 오류";
        return new(ExtractionFailureKind.Unknown, message, line);
    }

    /// <summary>stderr 라인들에서 마지막 ERROR 라인 추출 후 분류까지 한 번에.</summary>
    public static ExtractionFailure ClassifyStderr(IEnumerable<string> stderrLines)
        => Classify(ExtractLastErrorLine(stderrLines));

    private static bool Contains(string haystack, string needle)
        => haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);
}
