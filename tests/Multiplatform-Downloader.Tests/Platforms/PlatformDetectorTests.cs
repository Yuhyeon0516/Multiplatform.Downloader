using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Tests.Platforms;

public class PlatformDetectorTests
{
    private readonly PlatformDetector _sut = new();

    [Theory]
    [InlineData("https://www.youtube.com/watch?v=abc123", PlatformType.YouTube)]
    [InlineData("https://youtu.be/abc123", PlatformType.YouTube)]
    [InlineData("https://m.youtube.com/watch?v=abc123", PlatformType.YouTube)]
    [InlineData("https://music.youtube.com/watch?v=abc123", PlatformType.YouTube)]
    [InlineData("https://www.instagram.com/reel/Cxxxx/", PlatformType.Instagram)]
    [InlineData("https://instagr.am/p/Cxxxx/", PlatformType.Instagram)] // 공식 단축 도메인 (실측 FAIL → FR-D4.3 수정)
    [InlineData("https://www.tiktok.com/@user/video/123", PlatformType.TikTok)]
    [InlineData("https://vm.tiktok.com/ZSabcdef/", PlatformType.TikTok)]
    [InlineData("https://www.xiaohongshu.com/explore/abc", PlatformType.Xiaohongshu)]
    [InlineData("http://xhslink.com/a/abcd", PlatformType.Xiaohongshu)]
    [InlineData("https://www.rednote.com/explore/abc", PlatformType.Xiaohongshu)]
    public void should_detect_platform_when_supported_url(string url, PlatformType expected)
    {
        // Act
        var actual = _sut.Detect(url);

        // Assert
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("https://unknown-site.com/video")]
    [InlineData("https://youtube.com.evil.example/watch")]   // 유사 도메인 — 차단 (NFR-10)
    [InlineData("https://notyoutube.com/watch")]             // 접미 유사 — 차단
    [InlineData("ftp://youtube.com/x")]                      // http(s) 아님
    [InlineData("javascript:alert(1)")]                      // 스킴 공격
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void should_return_unknown_when_unsupported_or_malicious(string? url)
    {
        // Act
        var actual = _sut.Detect(url);

        // Assert
        Assert.Equal(PlatformType.Unknown, actual);
    }
}
