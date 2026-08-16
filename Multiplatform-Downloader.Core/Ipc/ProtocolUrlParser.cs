using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Core.Ipc;

/// <summary>
/// Chrome 확장이 여는 <c>mpdl://add?url=&lt;encoded&gt;</c> 프로토콜 URL을 파싱한다(FR-08).
/// 추출한 URL은 반드시 <see cref="PlatformDetector"/>(FR-01) 검증을 통과해야 하며(NFR-04),
/// percent-decoding은 한 번만 수행해 이중 디코딩 우회를 막는다(NFR-11).
/// </summary>
public sealed class ProtocolUrlParser
{
    private const string Scheme = "mpdl";
    private const string AddHost = "add";

    private readonly PlatformDetector _detector;

    public ProtocolUrlParser(PlatformDetector detector)
    {
        _detector = detector ?? throw new ArgumentNullException(nameof(detector));
    }

    /// <summary>유효하면 검증된 대상 URL을, 아니면 null을 반환한다.</summary>
    public string? Parse(string? protocolUrl)
    {
        if (string.IsNullOrWhiteSpace(protocolUrl))
            return null;
        if (!Uri.TryCreate(protocolUrl, UriKind.Absolute, out var uri))
            return null;
        if (!uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
            return null;
        if (!uri.Host.Equals(AddHost, StringComparison.OrdinalIgnoreCase))
            return null;

        var rawUrl = GetQueryValue(uri.Query, "url");
        if (rawUrl is null)
            return null;

        // 한 번만 디코딩 (이중 percent-decoding 방지)
        var decoded = Uri.UnescapeDataString(rawUrl);

        // FR-01 검증 통과한 지원 플랫폼 URL만 허용
        return _detector.Detect(decoded) == PlatformType.Unknown ? null : decoded;
    }

    private static string? GetQueryValue(string query, string key)
    {
        var trimmed = query.TrimStart('?');
        if (trimmed.Length == 0)
            return null;

        foreach (var pair in trimmed.Split('&'))
        {
            var separator = pair.IndexOf('=');
            if (separator < 0)
                continue;
            var name = pair[..separator];
            if (name.Equals(key, StringComparison.OrdinalIgnoreCase))
                return pair[(separator + 1)..];
        }
        return null;
    }
}
