using System.Text.RegularExpressions;

namespace Multiplatform_Downloader.Core.Update;

/// <summary>
/// 릴리스 태그(vX.Y.Z)와 어셈블리 버전(X.Y.Z.W)을 안전하게 파싱·비교하는 순수 함수(FR-U1).
/// 핵심 함정 회피: System.Version 기본 비교는 미지정 자리를 -1로 취급해
/// new Version("2.12.7") &lt; new Version("2.12.7.0")로 오판한다. 항상 4자리로 정규화 후 비교한다.
/// </summary>
public static partial class VersionComparer
{
    // vX.Y[.Z[.W]] — v 접두사 선택, 2~4자리. SemVer 접미사(-hotfix, +build)는 의도적으로 불허(보수적 거부).
    [GeneratedRegex(@"^v?(\d{1,9})\.(\d{1,9})(?:\.(\d{1,9}))?(?:\.(\d{1,9}))?$", RegexOptions.CultureInvariant)]
    private static partial Regex TagPattern();

    /// <summary>태그 문자열을 4자리 정규화 Version으로 파싱한다. 실패 시 false(예외 금지).</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
            return false;

        var m = TagPattern().Match(tag.Trim());
        if (!m.Success)
            return false;

        // 각 그룹은 정규식상 최대 9자리라 int 범위 안전. 미지정 자리는 0.
        var major = int.Parse(m.Groups[1].Value);
        var minor = int.Parse(m.Groups[2].Value);
        var build = m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0;
        var revision = m.Groups[4].Success ? int.Parse(m.Groups[4].Value) : 0;
        version = new Version(major, minor, build, revision);
        return true;
    }

    /// <summary>버전을 4자리로 정규화한다(미지정 자리 0). 어셈블리 Version 비교용.</summary>
    public static Version Normalize(Version v)
        => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));

    /// <summary>정규화 후 비교. 음수=left가 낮음, 0=동일, 양수=left가 높음.</summary>
    public static int Compare(Version left, Version right)
        => Normalize(left).CompareTo(Normalize(right));

    /// <summary>remote가 current보다 엄격히 상위 버전인가(업데이트 대상 여부의 순수 판정).</summary>
    public static bool IsNewer(Version remote, Version current)
        => Compare(remote, current) > 0;
}
