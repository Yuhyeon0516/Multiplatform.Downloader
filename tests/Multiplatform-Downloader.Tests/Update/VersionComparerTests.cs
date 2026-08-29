using Multiplatform_Downloader.Core.Update;

namespace Multiplatform_Downloader.Tests.Update;

public class VersionComparerTests
{
    [Theory] // UP-A01, A02, A04
    [InlineData("v2.12.8", 2, 12, 8, 0)]
    [InlineData("2.12.8", 2, 12, 8, 0)]     // v 접두사 선택
    [InlineData("v2.12.8.1", 2, 12, 8, 1)]  // 4자리
    [InlineData("v2.13", 2, 13, 0, 0)]      // 2자리
    public void should_parse_when_valid_tag(string tag, int a, int b, int c, int d)
    {
        Assert.True(VersionComparer.TryParseTag(tag, out var v));
        Assert.Equal(new Version(a, b, c, d), v);
    }

    [Theory] // UP-A05, A06, A10
    [InlineData("latest")]
    [InlineData("release-2.12")]
    [InlineData("v2..7")]
    [InlineData("v2.a.7")]
    [InlineData("v2.12.8-hotfix")]  // SemVer 접미사 보수적 거부
    [InlineData("v2.12.8+build.5")]
    [InlineData("")]
    [InlineData(null)]
    public void should_fail_when_invalid_tag(string? tag)
    {
        Assert.False(VersionComparer.TryParseTag(tag, out _));
    }

    [Fact] // UP-A03 — 3자리 태그 vs 4자리 어셈블리 버전 함정
    public void should_treat_equal_when_three_digit_tag_vs_four_digit_assembly()
    {
        VersionComparer.TryParseTag("v2.12.7", out var tag);
        var assembly = new Version(2, 12, 7, 0);
        Assert.Equal(0, VersionComparer.Compare(tag, assembly));
        Assert.False(VersionComparer.IsNewer(tag, assembly)); // 다운그레이드로 오판 금지
    }

    [Fact]
    public void should_detect_newer_when_higher_version()
    {
        VersionComparer.TryParseTag("v2.13.0", out var latest);
        Assert.True(VersionComparer.IsNewer(latest, new Version(2, 12, 7, 0)));
    }

    [Fact] // UP-A09 — 다운그레이드 방지
    public void should_not_be_newer_when_remote_is_lower()
    {
        VersionComparer.TryParseTag("v2.12.5", out var latest);
        Assert.False(VersionComparer.IsNewer(latest, new Version(2, 12, 7, 0)));
    }
}
