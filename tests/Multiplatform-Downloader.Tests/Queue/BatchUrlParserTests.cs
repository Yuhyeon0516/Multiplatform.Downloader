using Multiplatform_Downloader.Core.Platforms;
using Multiplatform_Downloader.Core.Queue;

namespace Multiplatform_Downloader.Tests.Queue;

public class BatchUrlParserTests
{
    private readonly BatchUrlParser _sut = new(new PlatformDetector());

    [Fact]
    public void should_split_lines_and_detect_platforms()
    {
        var text = """
        https://www.youtube.com/watch?v=abc
        https://vm.tiktok.com/ZSabc/
        https://www.instagram.com/reel/xyz/
        """;

        var result = _sut.Parse(text);

        Assert.Equal(3, result.Valid.Count);
        Assert.Contains(result.Valid, v => v.Platform == PlatformType.YouTube);
        Assert.Contains(result.Valid, v => v.Platform == PlatformType.TikTok);
        Assert.Contains(result.Valid, v => v.Platform == PlatformType.Instagram);
    }

    [Fact]
    public void should_reject_unsupported_urls()
    {
        var text = "https://www.youtube.com/watch?v=abc\nhttps://unknown-site.com/x";

        var result = _sut.Parse(text);

        Assert.Single(result.Valid);
        Assert.Single(result.Rejected);
        Assert.Equal("https://unknown-site.com/x", result.Rejected[0]);
    }

    [Fact]
    public void should_skip_duplicate_urls()
    {
        var text = "https://youtu.be/abc\nhttps://youtu.be/abc";

        var result = _sut.Parse(text);

        Assert.Single(result.Valid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void should_return_empty_when_blank(string? text)
    {
        var result = _sut.Parse(text);
        Assert.Empty(result.Valid);
        Assert.Empty(result.Rejected);
    }
}
