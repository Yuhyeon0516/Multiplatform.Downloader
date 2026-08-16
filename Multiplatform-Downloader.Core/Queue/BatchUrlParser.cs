using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Core.Queue;

public sealed record ParsedUrl(string Url, PlatformType Platform);

/// <summary>일괄 파싱 결과. <see cref="Rejected"/>는 미지원 플랫폼 URL.</summary>
public sealed record BatchParseResult(IReadOnlyList<ParsedUrl> Valid, IReadOnlyList<string> Rejected);

/// <summary>여러 줄 텍스트를 URL 목록으로 파싱한다(FR-05). 플랫폼 감지, 미지원·중복 제외.</summary>
public sealed class BatchUrlParser
{
    private readonly PlatformDetector _detector;

    public BatchUrlParser(PlatformDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    public BatchParseResult Parse(string? text)
    {
        var valid = new List<ParsedUrl>();
        var rejected = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
            return new BatchParseResult(valid, rejected);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawLine in text.Split('\n', '\r', '\t', ' '))
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            var platform = _detector.Detect(line);
            if (platform == PlatformType.Unknown)
            {
                rejected.Add(line);
                continue;
            }

            if (!seen.Add(line))
                continue; // 중복 스킵

            valid.Add(new ParsedUrl(line, platform));
        }

        return new BatchParseResult(valid, rejected);
    }
}
