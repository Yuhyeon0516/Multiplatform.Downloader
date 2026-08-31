using Multiplatform_Downloader.Core.Ipc;
using Multiplatform_Downloader.Core.Platforms;

namespace Multiplatform_Downloader.Tests.Ipc;

public class ProtocolUrlParserTests
{
    private readonly ProtocolUrlParser _sut = new(new PlatformDetector());

    [Fact]
    public void should_parse_url_from_mpdl_add()
    {
        var encoded = Uri.EscapeDataString("https://www.youtube.com/watch?v=abc");
        var result = _sut.Parse($"mpdl://add?url={encoded}");

        Assert.Equal("https://www.youtube.com/watch?v=abc", result);
    }

    [Fact]
    public void should_parse_xhslink_short_url()
    {
        var encoded = Uri.EscapeDataString("http://xhslink.com/a/abcd");
        var result = _sut.Parse($"mpdl://add?url={encoded}");

        Assert.Equal("http://xhslink.com/a/abcd", result);
    }

    [Theory]
    [InlineData("https://add?url=https://youtu.be/abc")]     // mpdl 아님
    [InlineData("mpdl://other?url=https://youtu.be/abc")]    // add 아님
    [InlineData("mpdl://add?nope=1")]                        // url 파라미터 없음
    [InlineData("not a uri")]
    [InlineData("")]
    [InlineData(null)]
    public void should_return_null_when_invalid_protocol(string? input)
    {
        Assert.Null(_sut.Parse(input));
    }

    [Fact]
    public void should_reject_unsupported_target_url()
    {
        var encoded = Uri.EscapeDataString("https://evil.example/x");
        Assert.Null(_sut.Parse($"mpdl://add?url={encoded}"));
    }

    [Fact]
    public void should_reject_non_http_target()
    {
        var encoded = Uri.EscapeDataString("file:///c:/windows");
        Assert.Null(_sut.Parse($"mpdl://add?url={encoded}"));
    }
}
