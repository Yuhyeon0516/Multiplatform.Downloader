using System.Text.Json;
using System.Text.RegularExpressions;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>Threads 자체 추출 실패.</summary>
public sealed class ThreadsExtractionException : Exception
{
    public ThreadsExtractionException(string message) : base(message) { }
}

/// <summary>Threads 자체 폴백 추출 결과.</summary>
public sealed record ThreadsExtractResult(string? Title, string? ThumbnailUrl, string StreamUrl);

/// <summary>
/// yt-dlp가 Threads 익스트랙터를 갖지 않으므로(실측 2026-08-02: extractor 부재), 게시물 HTML에
/// 서버 렌더된 <c>"video_versions":[…]</c> 배열에서 스트림 URL을 직접 파싱한다(FR-N1.8).
/// <para>실측 사실: 완전한 브라우저 네비게이션 헤더(Sec-Fetch-*·sec-ch-ua·Accept:text/html)로
/// 요청해야 서버가 이 JSON을 렌더한다(UA만 보내면 JS 셸). type 101(최고 프로그레시브)을 우선한다.
/// URL 이스케이프는 표준 JSON(<c>\/</c> 등) — 반드시 JSON 파서로 디코드한다.</para>
/// <para>CDN URL은 서명 만료형(oh/oe) — 추출 즉시 다운로드해야 한다.</para>
/// </summary>
public sealed class ThreadsFallbackExtractor
{
    // "video_versions":[ ... ] — 비탐욕 매칭으로 첫 배열 캡처(각 원소는 {type,url,width,height})
    private static readonly Regex VideoVersionsRegex = new(
        "\"video_versions\"\\s*:\\s*(\\[.*?\\])", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex TitleRegex = new(
        "\"(?:caption|accessibility_caption|title)\"\\s*:\\s*(\"(?:\\\\.|[^\"\\\\])*\")",
        RegexOptions.Compiled);

    // 실측(2026-08-02): 포스터 이미지는 "image_versions2":{"candidates":[{...,"url":"https:\/\/...jpg"}]}
    // 구조에 있다(cover_image_url 등은 없음). image_versions2 직후 첫 이미지 url을 최대 후보로 취한다.
    private static readonly Regex ThumbnailRegex = new(
        "\"image_versions2\".*?\"url\"\\s*:\\s*\"(https[^\"]*?\\.(?:jpg|jpeg|webp|png)[^\"]*)\"",
        RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>게시물 HTML에서 스트림 URL과 메타데이터를 추출한다.</summary>
    /// <exception cref="ThreadsExtractionException">video_versions·스트림 URL을 찾지 못할 때.</exception>
    public ThreadsExtractResult Extract(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        var match = VideoVersionsRegex.Match(html);
        if (!match.Success)
            throw new ThreadsExtractionException(
                "영상 정보(video_versions)를 찾을 수 없습니다 — 영상이 없는 게시물이거나 페이지 구조 변경(완전한 브라우저 헤더 필요).");

        JsonElement array;
        try
        {
            using var doc = JsonDocument.Parse(match.Groups[1].Value);
            array = doc.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new ThreadsExtractionException($"video_versions JSON 파싱 실패: {ex.Message}");
        }

        var streamUrl = SelectBestUrl(array)
            ?? throw new ThreadsExtractionException("재생 가능한 스트림 URL이 없습니다.");

        return new ThreadsExtractResult(ExtractTitle(html), ExtractThumbnail(html), streamUrl);
    }

    /// <summary>type 101(최고 프로그레시브) → 102 → 103 → 배열 첫 원소 순으로 URL 선택.</summary>
    private static string? SelectBestUrl(JsonElement array)
    {
        if (array.ValueKind != JsonValueKind.Array)
            return null;

        string? firstUrl = null;
        foreach (var preferredType in new[] { 101, 102, 103 })
        {
            foreach (var item in array.EnumerateArray())
            {
                if (!item.TryGetProperty("url", out var urlProp) || urlProp.ValueKind != JsonValueKind.String)
                    continue;
                var url = urlProp.GetString();
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                firstUrl ??= url;
                if (item.TryGetProperty("type", out var typeProp)
                    && typeProp.TryGetInt32(out var type) && type == preferredType)
                    return url;
            }
        }
        return firstUrl; // type 미표기 응답 대비 — 첫 유효 URL
    }

    private static string? ExtractTitle(string html)
    {
        var m = TitleRegex.Match(html);
        if (!m.Success) return null;
        try { return JsonSerializer.Deserialize<string>(m.Groups[1].Value); }
        catch (JsonException) { return null; }
    }

    private static string? ExtractThumbnail(string html)
    {
        var m = ThumbnailRegex.Match(html);
        if (!m.Success) return null;
        var raw = m.Groups[1].Value.Replace("\\/", "/");
        return string.IsNullOrWhiteSpace(raw) ? null : raw;
    }
}
