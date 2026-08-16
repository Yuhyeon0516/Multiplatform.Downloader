using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Multiplatform_Downloader.Core.Models;

namespace Multiplatform_Downloader.Core.Engine;

/// <summary>yt-dlp <c>-J</c> JSON 및 <c>--newline</c> 진행률 출력을 파싱한다(FR-02·FR-04).</summary>
public static partial class YtDlpOutputParser
{
    [GeneratedRegex(@"(\d+(?:\.\d+)?)%", RegexOptions.CultureInvariant)]
    private static partial Regex PercentRegex();

    [GeneratedRegex(@"at\s+([\d.]+)\s*([KMGT]?i?B)/s", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SpeedRegex();

    [GeneratedRegex(@"ETA\s+(?:(\d+):)?(\d+):(\d+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EtaRegex();

    /// <summary>
    /// yt-dlp <c>--newline</c> 진행률 라인을 파싱한다. 진행률 라인이 아니면 null.
    /// 예: <c>[download]  42.3% of ~10.00MiB at 1.23MiB/s ETA 00:41</c>
    /// </summary>
    public static DownloadProgress? ParseProgressLine(string? line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("[download]", StringComparison.Ordinal))
            return null;

        var percentMatch = PercentRegex().Match(line);
        if (!percentMatch.Success)
            return null;

        var percent = double.Parse(percentMatch.Groups[1].Value, CultureInfo.InvariantCulture);

        long? speed = null;
        var speedMatch = SpeedRegex().Match(line);
        if (speedMatch.Success)
        {
            var value = double.Parse(speedMatch.Groups[1].Value, CultureInfo.InvariantCulture);
            speed = (long)(value * UnitToBytes(speedMatch.Groups[2].Value));
        }

        TimeSpan? eta = null;
        var etaMatch = EtaRegex().Match(line);
        if (etaMatch.Success)
        {
            var hours = etaMatch.Groups[1].Success ? int.Parse(etaMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var minutes = int.Parse(etaMatch.Groups[2].Value, CultureInfo.InvariantCulture);
            var seconds = int.Parse(etaMatch.Groups[3].Value, CultureInfo.InvariantCulture);
            eta = new TimeSpan(hours, minutes, seconds);
        }

        return new DownloadProgress(percent, speed, eta);
    }

    private static double UnitToBytes(string unit) => unit.ToUpperInvariant() switch
    {
        "B" => 1d,
        "KIB" => 1024d,
        "KB" => 1000d,
        "MIB" => 1024d * 1024,
        "MB" => 1_000_000d,
        "GIB" => 1024d * 1024 * 1024,
        "GB" => 1_000_000_000d,
        "TIB" => 1024d * 1024 * 1024 * 1024,
        _ => 1d,
    };

    /// <summary>yt-dlp <c>-J</c> JSON을 파싱한다.</summary>
    /// <exception cref="FormatException">빈 문자열이거나 유효하지 않은 JSON일 때.</exception>
    public static MediaInfo ParseInfo(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new FormatException("메타데이터 JSON이 비어 있습니다.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new FormatException($"메타데이터 JSON 형식 오류: {ex.Message}", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new FormatException("메타데이터 JSON 최상위가 객체가 아닙니다.");

            var thumbnails = SelectThumbnailCandidates(root);
            return new MediaInfo
            {
                Id = GetString(root, "id"),
                Title = GetString(root, "title"),
                ThumbnailUrl = thumbnails.Count > 0 ? thumbnails[0] : null,
                ThumbnailUrls = thumbnails,
                Duration = GetDouble(root, "duration") is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
                Formats = ParseFormats(root),
            };
        }
    }

    private static IReadOnlyList<MediaFormat> ParseFormats(JsonElement root)
    {
        var formats = new List<MediaFormat>();
        if (!root.TryGetProperty("formats", out var formatsElement) || formatsElement.ValueKind != JsonValueKind.Array)
            return formats;

        foreach (var element in formatsElement.EnumerateArray())
        {
            var formatId = GetString(element, "format_id");
            if (formatId is null)
                continue;

            var videoCodec = GetString(element, "vcodec");
            var audioCodec = GetString(element, "acodec");

            formats.Add(new MediaFormat
            {
                FormatId = formatId,
                Ext = GetString(element, "ext"),
                Height = GetInt(element, "height"),
                Width = GetInt(element, "width"),
                Fps = GetDouble(element, "fps"),
                VideoCodec = NormalizeCodec(videoCodec),
                AudioCodec = NormalizeCodec(audioCodec),
                ApproxSize = GetLong(element, "filesize") ?? GetLong(element, "filesize_approx"),
                IsAudioOnly = IsNone(videoCodec) && !IsNone(audioCodec),
                IsVideoOnly = !IsNone(videoCodec) && IsNone(audioCodec),
            });
        }
        return formats;
    }

    /// <summary>
    /// 썸네일 후보 목록을 우선순위 내림차순으로 만든다(FR-D1.1).
    /// 확장자 화이트리스트는 폐지 — 실측 결과 XHS 썸네일은 확장자가 아예 없어 전원 탈락했었다.
    /// 수용 여부는 다운로드 후 매직바이트로 판정한다(<c>ImageSniffer</c>).
    /// 정렬: ①확장자가 jpg/png(WPF 즉시 디코드 가능)면 가산 ②면적(width*height) ③배열 뒤쪽(yt-dlp는 뒤가 고화질).
    /// 최상위 <c>thumbnail</c>은 중복이 아니면 마지막 폴백으로 덧붙인다.
    /// </summary>
    private static IReadOnlyList<string> SelectThumbnailCandidates(JsonElement root)
    {
        var scored = new List<(string Url, long Score)>();
        if (root.TryGetProperty("thumbnails", out var thumbs) && thumbs.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var t in thumbs.EnumerateArray())
            {
                var url = GetString(t, "url");
                if (string.IsNullOrWhiteSpace(url))
                    continue;
                long area = (long)(GetInt(t, "width") ?? 0) * (GetInt(t, "height") ?? 0);
                // jpg/png는 변환 없이 즉시 디코드 가능 → 큰 가산점(같은 이미지의 webp 변형보다 우선)
                long bonus = HasWpfFriendlyExtension(url) ? 1_000_000_000L : 0;
                scored.Add((url, bonus + area * 10 + index)); // 면적 동률이면 배열 뒤쪽 우선
                index++;
            }
        }

        var result = scored
            .OrderByDescending(s => s.Score)
            .Select(s => s.Url)
            .Distinct(StringComparer.Ordinal)
            .Take(4)
            .ToList();

        var top = GetString(root, "thumbnail");
        if (!string.IsNullOrWhiteSpace(top) && !result.Contains(top, StringComparer.Ordinal))
            result.Add(top);

        return result;
    }

    /// <summary>경로가 WPF 즉시 디코드 확장자(jpg/png 계열)로 끝나는지 — 우선순위 가산용(거부 목적 아님).</summary>
    private static bool HasWpfFriendlyExtension(string url)
    {
        var path = url;
        var q = path.IndexOf('?');
        if (q >= 0) path = path[..q];
        return path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNone(string? codec) => string.IsNullOrEmpty(codec) || codec == "none";
    private static string? NormalizeCodec(string? codec) => IsNone(codec) ? null : codec;

    private static string? GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static int? GetInt(JsonElement element, string name)
        => element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out var v) ? v : null;

    private static long? GetLong(JsonElement element, string name)
        => element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt64(out var v) ? v : null;

    private static double? GetDouble(JsonElement element, string name)
        => element.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out var v) ? v : null;
}
