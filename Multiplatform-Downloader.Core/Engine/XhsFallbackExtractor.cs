using System.Text.Json;
using System.Text.RegularExpressions;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>샤오홍슈 자체 추출 실패.</summary>
public sealed class XhsExtractionException : Exception
{
    public XhsExtractionException(string message) : base(message) { }
}

/// <summary>샤오홍슈 자체 폴백 추출 결과.</summary>
public sealed record XhsExtractResult(string? Title, string? ThumbnailUrl, string StreamUrl);

/// <summary>
/// yt-dlp가 실패할 때 샤오홍슈 페이지 HTML의 초기 상태 JSON에서 스트림 URL을 직접 파싱한다(FR-13).
/// ⚠ 페이지 구조는 VER-08에서 실제 캡처로 검증해야 하며, 현재 파싱 경로·픽스처는 추정이다.
/// XHS가 구조를 바꾸면 여기와 픽스처를 함께 갱신한다(유닛 테스트로 조기 감지).
/// </summary>
public sealed class XhsFallbackExtractor
{
    private const string StateMarker = "__INITIAL_STATE__";

    // 실측(2026-08-02): XHS __INITIAL_STATE__ 는 JS 객체라 값에 undefined 가 들어있다
    // (JSON 아님 → JsonDocument.Parse 가 'u'에서 실패). 값 위치의 undefined 를 null 로 치환한다.
    private static readonly Regex UndefinedValue = new(
        @"(?<=[:\[,])\s*undefined\b", RegexOptions.Compiled);

    /// <summary>페이지 HTML에서 스트림 URL과 메타데이터를 추출한다.</summary>
    /// <exception cref="XhsExtractionException">초기 상태 JSON·스트림 URL을 찾지 못할 때.</exception>
    public XhsExtractResult Extract(string html)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(html);

        var json = ExtractInitialStateJson(html)
            ?? throw new XhsExtractionException("초기 상태 JSON을 찾을 수 없습니다 (페이지 구조 변경 가능).");

        json = UndefinedValue.Replace(json, "null"); // JS undefined → JSON null (XHS 상태 객체 정규화)

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new XhsExtractionException($"초기 상태 JSON 파싱 실패: {ex.Message}");
        }

        using (document)
        {
            // 실측(2026-08-02): 실제 노트 데이터는 noteDetailMap.<id>.note 에 있다. 여기서 스코프해서
            // 제목/커버/영상을 뽑아야 한다. 그러지 않으면 페이지 상단의 generic "title":"搜索小红书"(검색 UI)나
            // 엉뚱한 이미지 키를 긁게 된다(사용자 보고: 폴백 노트 제목·썸네일 오류).
            var note = TryGetNote(document.RootElement);

            string? title;
            string? thumbnail;
            string? streamUrl;
            if (note is { } n)
            {
                title = GetStringProp(n, "title") ?? GetStringProp(n, "desc");
                thumbnail = ExtractCover(n);
                streamUrl = FindFirstVideoUrl(n); // 노트 스코프 내에서만 탐색(video·라이브포토 stream 포함)
            }
            else
            {
                // noteDetailMap 미발견 — 구버전/변경 대비 전체 문서 탐색으로 폴백
                title = FindFirstStringByKey(document.RootElement, "title");
                thumbnail = FindFirstStringByKey(document.RootElement, "cover")
                    ?? FindFirstStringByKey(document.RootElement, "thumbnail");
                streamUrl = FindFirstVideoUrl(document.RootElement);
            }

            if (streamUrl is null)
            {
                // 영상/라이브포토 stream 이 전혀 없으면 순수 사진 노트 — 명확히 안내
                var isPhoto = json.Contains("\"type\":\"normal\"", StringComparison.Ordinal);
                throw new XhsExtractionException(isPhoto
                    ? "사진 노트입니다 (영상 없음) — 영상 노트를 받아주세요"
                    : "스트림 URL을 찾을 수 없습니다 (로그인/미지원 게시물 가능).");
            }
            return new XhsExtractResult(title, thumbnail, streamUrl);
        }
    }

    /// <summary>noteDetailMap 의 첫 항목의 note 객체를 반환한다(없으면 null).</summary>
    private static JsonElement? TryGetNote(JsonElement root)
    {
        var ndm = FindProperty(root, "noteDetailMap");
        if (ndm is not { ValueKind: JsonValueKind.Object } map)
            return null;
        foreach (var entry in map.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object
                && entry.Value.TryGetProperty("note", out var note)
                && note.ValueKind == JsonValueKind.Object)
                return note;
        }
        return null;
    }

    /// <summary>노트의 커버 이미지 URL: imageList[0].urlDefault → urlPre → url.</summary>
    private static string? ExtractCover(JsonElement note)
    {
        if (note.TryGetProperty("imageList", out var list)
            && list.ValueKind == JsonValueKind.Array && list.GetArrayLength() > 0)
        {
            var first = list[0];
            foreach (var key in new[] { "urlDefault", "urlPre", "url" })
            {
                var v = GetStringProp(first, key);
                if (!string.IsNullOrWhiteSpace(v) && v!.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    return v;
            }
        }
        // 영상 노트 커버 폴백
        return GetStringProp(note, "cover") ?? GetStringProp(note, "thumbnail");
    }

    private static string? GetStringProp(JsonElement obj, string key)
        => obj.ValueKind == JsonValueKind.Object
           && obj.TryGetProperty(key, out var v)
           && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    /// <summary>깊이 우선으로 첫 번째 지정 키 프로퍼티를 찾는다.</summary>
    private static JsonElement? FindProperty(JsonElement element, string key)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in element.EnumerateObject())
                {
                    if (p.NameEquals(key)) return p.Value;
                    var nested = FindProperty(p.Value, key);
                    if (nested is not null) return nested;
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindProperty(item, key);
                    if (nested is not null) return nested;
                }
                break;
        }
        return null;
    }

    /// <summary>
    /// <c>__INITIAL_STATE__ = { ... }</c>의 balanced-brace JSON을 추출한다.
    /// (JSON 문자열 내부의 중괄호는 고려하지 않는 단순 매칭 — 실제 페이지 검증 후 강화 필요.)
    /// </summary>
    private static string? ExtractInitialStateJson(string html)
    {
        var markerIndex = html.IndexOf(StateMarker, StringComparison.Ordinal);
        if (markerIndex < 0) return null;

        var equalsIndex = html.IndexOf('=', markerIndex);
        if (equalsIndex < 0) return null;

        var start = html.IndexOf('{', equalsIndex);
        if (start < 0) return null;

        var depth = 0;
        for (var i = start; i < html.Length; i++)
        {
            if (html[i] == '{') depth++;
            else if (html[i] == '}')
            {
                depth--;
                if (depth == 0)
                    return html[start..(i + 1)];
            }
        }
        return null;
    }

    private static string? FindFirstVideoUrl(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if ((property.NameEquals("masterUrl") || property.NameEquals("videoUrl") || property.NameEquals("backupUrls"))
                        && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var candidate = property.Value.GetString();
                        // http(s) 스킴만 신뢰 — 다운로드 단계 SSRF 방어와 일관(M5)
                        if (candidate is not null && candidate.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                            return candidate;
                    }
                    var nested = FindFirstVideoUrl(property.Value);
                    if (nested is not null) return nested;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindFirstVideoUrl(item);
                    if (nested is not null) return nested;
                }
                break;

            case JsonValueKind.String:
                var value = element.GetString();
                if (value is not null
                    && value.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    && value.Contains(".mp4", StringComparison.OrdinalIgnoreCase))
                {
                    return value;
                }
                break;
        }
        return null;
    }

    private static string? FindFirstStringByKey(JsonElement element, string key)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    if (property.NameEquals(key) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        var text = property.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                    var nested = FindFirstStringByKey(property.Value, key);
                    if (nested is not null) return nested;
                }
                break;

            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindFirstStringByKey(item, key);
                    if (nested is not null) return nested;
                }
                break;
        }
        return null;
    }
}
